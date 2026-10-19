using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;
using AdLib.IO;
using AdLib.IO.Messages;
using AdLib.Utils;

namespace AdLib.Model;

public sealed class FileTransferClient : IAsyncDisposable
{
    public class FileListingReceivedEventArgs : EventArgs
    {
        public required string Path { get; init; }
        public required FileEntry[] Files { get; init; }
    }

    private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Random _random = new();
    private readonly ConcurrentQueue<ClientRequest> _requests = new();
    private readonly Task _thread;
    private readonly CancellationTokenSource _selfCancel = new();

    private readonly bool _ownsMessageChannel;
    private SecureClient? _secureClient;
    private IMessageChannel? _messageChannel;

    private string _latestDownload = "";
    
    public bool IsConnected { get; private set; }
    public string? ServerFolder { get; private set; }
    public char ServerPathSeparator { get; private set; }

    public async ValueTask DisposeAsync()
    {
        await this._thread;
        
        this._secureClient?.Dispose();

        Task.WaitAll(this._activeDownloads.Values.Select(p => p.stream.DisposeAsync().AsTask()));
        foreach ((string partPath, FileStream stream) download in this._activeDownloads.Values)
        {
            await download.stream.DisposeAsync();
        }

        this._activeDownloads.Clear();
        this._communicationHandle.Dispose();

        if (this._ownsMessageChannel)
        {
            this._messageChannel?.Dispose();
        }
    }

    public event EventHandler<FileListingReceivedEventArgs>? FileListingReceived;
    public event SecureConnectionUtils.AuthenticationErrorHandler? AuthenticationError;
    public event Action<string>? GracefullyDisconnected;
    public event Action<string>? ForceDisconnected;
    public event Action<string>? RecoverableError;

    public event Action<string>? Connected;
    public event Action<string>? FileSending;
    public event Action<string>? FileReceiving;

    // allowed to run by default
    private readonly SemaphoreSlim _communicationHandle = new(1);

    private event EventHandler<EventArgs<ClientRequest>>? RequestEntered;

    public FileTransferClient(string host, Identity identity, TrustStore store)
    {
        this._ownsMessageChannel = true;

        this._thread = Task.Run(async () =>
        {
            try
            {
                IMessageChannel? messageChannel = await this.Connect(host, identity, store);

                // authentication failed (or no connection could be made) - the relevant event was already raised
                if (messageChannel is null)
                {
                    return;
                }
                else
                {
                    this._messageChannel = messageChannel;
                }

                await this.Listen(messageChannel, host);
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException
                                          or AggregateException
                                          {
                                              InnerException: TaskCanceledException or OperationCanceledException,
                                          })
            {
                // ignore cancellations
            }
            finally
            {
                this.Disconnect(); // does not fire events nor send messages, also frees managed resources
            }
        }, this._selfCancel.Token);
    }

    /// <summary>
    ///     Starts listening for messages on an already-established channel, without performing any connection
    ///     setup. Useful if the caller owns the connection/channel (for example, in tests).
    /// </summary>
    /// <param name="messageChannel">the channel messages are read from and written to</param>
    /// <param name="host">the host name reported to <see cref="Connected" />; informational only</param>
    public FileTransferClient(IMessageChannel messageChannel, string host = "")
    {
        this._ownsMessageChannel = false;
        this._messageChannel = messageChannel;

        this._thread = Task.Run(async () =>
        {
            try
            {
                await this.Listen(messageChannel, host);
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException
                                          or AggregateException
                                          {
                                              InnerException: TaskCanceledException or OperationCanceledException,
                                          })
            {
                // ignore cancellations
            }
            finally
            {
                this.Disconnect();
            }
        }, this._selfCancel.Token);
    }

    /// <summary>
    ///     Performs the connection setup and returns the resulting message channel, or <c>null</c> if the
    ///     connection was not successful.
    /// </summary>
    private async Task<IMessageChannel?> Connect(string host, Identity identity, TrustStore store)
    {
        this._secureClient = new SecureClient(identity, store);

        SecureConnectionUtils.ConnectionInfo info = await this._secureClient.ConnectAsync(host);

        SecureConnection? connection = info.Connection;
        SecureConnectionUtils.ConnectionResult result = info.Result;
        SecureConnectionUtils.RejectionReason reason = info.Reason;

        // connection failed - raise the event then bail (stream is expected to be null) - does not return
        if (result != SecureConnectionUtils.ConnectionResult.Success ||
            reason != SecureConnectionUtils.RejectionReason.None)
        {
            if (info.PublicKey is null)
            {
                IOException ex = new("Remote host did not offer a certificate");
                this.ForceDisconnect(ex);
                throw ex;
            }

            // handles adding trust, showing fingerprint, error dialogues etc.
            this.AuthenticationError?.Invoke(
                host, store.FindPublicKeyOrDefault(info.PublicKey), info.PublicKey, result, reason
            );

            return null;
        }

        // quick sanity check
        if (connection is null || this._secureClient.Channel is null)
        {
            // stream is null but no error was thrown - this is a bug
            InvalidOperationException ex = new("TLS connection is null after creation");
            this.ForceDisconnect(ex);
            throw ex;
        }

        return new BlockMessageChannel(new SshDataBlockChannel(this._secureClient.Channel));
    }

    public void AddRequest(ClientRequest request)
    {
        this._requests.Enqueue(request);
        this.RequestEntered?.Invoke(this, new EventArgs<ClientRequest> { Value = request });
    }

    private async Task Listen(IMessageChannel messageChannel, string host)
    {
        this._messageChannel = messageChannel;
        this._messageChannel.MessageReceived += (_, _) => this._communicationHandle.Release();

        // allows thread to continue accepting requests
        this.RequestEntered += (_, _) => this._communicationHandle.Release();

        // do handshake
        await this.SendMessage(new InitMessage { PathSeparator = Path.PathSeparator });
        IMessage ackMsg = await this._messageChannel.ReadMessageAsync();

        if (ackMsg is not InitAckMessage acknowledgement)
        {
            throw new InvalidOperationException($"Expected InitAckMessage, got {ackMsg.Header}");
        }

        this.ServerFolder = acknowledgement.SharedFolderPath;
        this.ServerPathSeparator = acknowledgement.PathSeparator;

        // this is the point where we are "connected" (successfully connected to a trusted & funcitoning host)
        this.Connected?.Invoke(host);
        this.IsConnected = true;

        while (!this._cancellationTokenSource.IsCancellationRequested)
        {
            bool tryAgainImmediately = false;
            IMessage? message = this._messageChannel.ReadMessageIfAvailable();

            while (message is not null)
            {
                await this.HandleServerMessage(message);
                message = this._messageChannel.ReadMessageIfAvailable();
            }

            while (this._requests.TryDequeue(out ClientRequest request))
            {
                // always prioritize server messages over requests, since other messages could throw off
                // certain flows/transactions used by the server
                if (this._messageChannel.HasMessage())
                {
                    tryAgainImmediately = true;
                    continue;
                }

                await this.HandleRequest(request);
            }

            // unset when a message/request is entered
            if (!tryAgainImmediately)
            {
                await this._communicationHandle.WaitAsync();
            }
        }
    }

    private async Task SendMessage(IMessage message, CancellationToken ct = default)
    {
        if (this._messageChannel is null)
        {
            throw new InvalidOperationException("Message channel is not set - (should be) unreachable");
        }
        
        try
        {
            await this._messageChannel.WriteMessageAsync(message, ct);
        }
        catch (Exception ex)
        {
            await this.CloseSessionAfterError(ex, FatalError.IOError);
            throw;
        }
    }

    private async Task ExpectAcknowledgement(MessageType type)
    {
        if (this._messageChannel is null)
        {
            throw new InvalidOperationException("Message channel is not set - (should be) unreachable");
        }

        try
        {
            IMessage message = await this._messageChannel.ReadMessageAsync();

            if (message is not ControlAckMessage acknowledgement)
            {
                throw new IOException($"Expected acknowledgement, got {message.Header}");
            }

            if (acknowledgement.ControlCode != (byte)type)
            {
                throw new IOException($"Expected {type} acknowledgement, got {acknowledgement.ControlCode}");
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            await this.CloseSessionAfterError(ex, FatalError.IOError);
        }
    }

    private async Task HandleRequest(ClientRequest request)
    {
        switch (request.Type)
        {
            case ClientRequestType.PutPath:
                await this.UploadPath(request.Path, Path.GetFileName(request.Path));
                break;

            case ClientRequestType.GetPath:
                await this.SendMessage(new FileRequestMessage { Path = request.Path });
                break;

            case ClientRequestType.DeleteRemotePath:
                await this.SendMessage(new DeleteMessage { Path = request.Path });
                await this.ExpectAcknowledgement(MessageType.Delete);
                break;

            case ClientRequestType.MakeRemoteDir:
                await this.SendMessage(new MakeDirMessage { Path = request.Path });
                await this.ExpectAcknowledgement(MessageType.MakeDir);
                break;

            case ClientRequestType.ListFiles:
                await this.SendMessage(new ListFilesMessage { Path = request.Path });
                break;

            case ClientRequestType.Disconnect:
                await this.SendMessage(new EndMessage());
                break;
        }
    }

    private async Task HandleServerMessage(IMessage message)
    {
        switch (message)
        {
            case InitAckMessage init:
                this.ServerFolder = init.SharedFolderPath;
                break;

            case ErrorFatalMessage fatal:
                await this._cancellationTokenSource.CancelAsync();
                this.IsConnected = false;
                this.ForceDisconnected?.Invoke($"Fatal server error (error code 0x{fatal.Errno:X8})");
                break;

            case ErrorRecoverableMessage recoverable:
                this.RecoverableError?.Invoke($"{recoverable.Errno}");
                break;

            case EndMessage:
                await this.SendMessage(new EndAckMessage());
                this.GracefullyDisconnected?.Invoke("Disconnected by server");
                await this._cancellationTokenSource.CancelAsync();
                this.IsConnected = false;
                break;

            case EndAckMessage:
                this.GracefullyDisconnected?.Invoke("Disconnected");
                this.IsConnected = false;
                break;

            case ListFilesResponseMessage listFiles:
                this.FileListingReceived?.Invoke(this, new FileListingReceivedEventArgs
                {
                    Path = listFiles.Path,
                    Files = listFiles.Files,
                });

                break;

            case DataMessage data:
                await this.HandleIncomingData(data);
                break;

            case DataFinishedMessage finished:
                this.FinalizeDownload(finished.Path);
                break;

            case MakeDirMessage makeDir:
                FileTransferUtils.CreateDirectory(makeDir.Path);
                break;

            case StatusRequestMessage status: // not used by server
                await this.SendMessage(new StatusResponseMessage { Random = status.Random });
                break;

            case ResendRequestMessage resend:
                await FileTransferUtils.ResendBlockAsync(resend, this.SendMessage);
                break;

            case ControlAckMessage ack: // will manually be expected
                throw new InvalidOperationException($"Unexpected acknowledgement for {ack.ControlCode}");

            default:
                throw new InvalidOperationException($"Unknown message type: {message.GetType()}");
        }
    }

    /// <summary>
    ///     Instantly terminate a session and free all managed resources
    /// </summary>
    private void Disconnect()
    {
        this._selfCancel.Cancel();

        this._messageChannel?.Dispose();
        this._secureClient?.Dispose();
        this._cancellationTokenSource.Cancel();
        this._cancellationTokenSource.Dispose();
        this.IsConnected = false;
    }

    /// <summary>
    ///     Terminate a session and free all managed resources, then fire an event
    /// </summary>
    /// <param name="ex">the causing exception</param>
    private void ForceDisconnect(Exception ex) => this.ForceDisconnect($"{ex.GetType()}: {ex.Message}");

    /// <summary>
    ///     Terminate a session and free all managed resources, then fire an event
    /// </summary>
    /// <param name="msg">the message to send that explains this disconnection</param>
    private void ForceDisconnect(string msg)
    {
        this.Disconnect();
        this.ForceDisconnected?.Invoke(msg);
    }

    /// <summary>
    ///     Close (send an error then terminate) the session
    /// </summary>
    /// <param name="ex">the causing exception</param>
    /// <param name="errno">the error code to send</param>
    private async Task CloseSessionAfterError(Exception ex, FatalError errno) =>
        await this.CloseSessionAfterError($"{ex.GetType()}: {ex.Message}", errno);

    /// <summary>
    ///     Close (send an error then terminate) the session
    /// </summary>
    /// <param name="msg">the message to send that explains this session close</param>
    /// <param name="errno">the error code to send</param>
    private async Task CloseSessionAfterError(string msg, FatalError errno)
    {
        if (this._secureClient?.Channel is not null) // true if connected + handshake completed before error
        {
            await this.SendMessage(new ErrorFatalMessage { Errno = errno });
        }

        this.ForceDisconnect(msg);
    }

    private async Task HandleIncomingData(DataMessage data)
    {
        if (this._latestDownload != data.Path)
        {
            this._latestDownload = data.Path;
            this.FileReceiving?.Invoke(data.Path);
        }

        await FileTransferUtils.ProcessDownloadChunkAsync(data, this._activeDownloads, this._random, this.SendMessage);
    }

    private void FinalizeDownload(string path) { FileTransferUtils.FinalizeDownload(path, this._activeDownloads); }

    private async Task UploadPath(string localPath, string remotePath)
    {
        this.FileSending?.Invoke(localPath);
        await FileTransferUtils.UploadPathAsync(localPath, remotePath, this.SendMessage);
    }
}