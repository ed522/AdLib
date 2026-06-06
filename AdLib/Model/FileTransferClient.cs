using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;
using AdLib.IO;
using AdLib.IO.Messages;

namespace AdLib.Model;

public sealed class FileTransferClient : IDisposable
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
    private readonly ConcurrentQueue<IMessage> _messages = new();

    private readonly MemoryStream _partialBuffer = new();
    private uint _currentExpectedSize;

    private string _latestDownload = "";
    private SecureClient? _secureClient;
    public bool IsConnected { get; private set; }
    public string? ServerFolder { get; private set; }

    public void Dispose()
    {
        this._secureClient?.Dispose();

        foreach ((string partPath, FileStream stream) download in this._activeDownloads.Values)
        {
            download.stream.Dispose();
        }

        this._activeDownloads.Clear();
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

    private class MessageReceivedEventArgs : EventArgs
    {
        public required IMessage Message { get; init; }
    }

    private class RequestEnteredEventArgs : EventArgs
    {
        public required ClientRequest Request { get; init; }
    }

    private event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    private event EventHandler<RequestEnteredEventArgs>? RequestEntered;

    public void AddRequest(ClientRequest request)
    {
        this._requests.Enqueue(request);
        this.RequestEntered?.Invoke(this, new RequestEnteredEventArgs { Request = request });
    }

    public Task ConnectAndListen(string host, Identity identity, TrustStore store)
    {
        Task thread = Task.Run(async () =>
        {
            try
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
                        this.CloseAfterError(ex);
                        throw ex;
                    }

                    // handles adding trust, showing fingerprint, error dialogues etc.
                    this.AuthenticationError?.Invoke(
                        host, store.FindPublicKeyOrDefault(info.PublicKey), info.PublicKey, result, reason
                    );

                    return;
                }

                // quick sanity check
                if (connection is null || this._secureClient.Channel is null)
                {
                    // stream is null but no error was thrown - this is a bug
                    InvalidOperationException ex = new("TLS connection is null after creation");
                    this.CloseAfterError(ex);
                    throw ex;
                }

                connection.Channel.DataReceived += (_, args) =>
                {
                    this._currentExpectedSize = FileTransferUtils.BufferMessage(
                        this._currentExpectedSize,
                        args.Array,
                        this._partialBuffer,
                        out IMessage? message
                    );

                    connection.Channel.AdjustWindow((uint)args.Array.Length);

                    // not null on complete message, null while buffering
                    if (message is not null)
                    {
                        this._messages.Enqueue(message);
                        this.MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                    }
                };

                // allows thread to continue accepting requests
                this.MessageReceived += (_, _) => this._communicationHandle.Release();
                this.RequestEntered += (_, _) => this._communicationHandle.Release();

                // do handshake
                await this.SendMessage(new InitMessage());
                IMessage ackMsg = await this.WaitForMessage();

                if (ackMsg is not InitAckMessage acknowledgement)
                {
                    throw new InvalidOperationException($"Expected InitAckMessage, got {ackMsg.Header}");
                }

                this.ServerFolder = acknowledgement.SharedFolderPath;

                // this is the point where we are "connected" (successfully connected to a trusted & funcitoning host)
                this.Connected?.Invoke(host);
                this.IsConnected = true;

                while (!this._cancellationTokenSource.IsCancellationRequested)
                {
                    while (this._messages.TryDequeue(out IMessage? message))
                    {
                        await this.HandleServerMessage(message);
                    }

                    while (this._requests.TryDequeue(out ClientRequest request))
                    {
                        // always prioritize server messages over requests, since other messages could throw off
                        // certain flows/transactions used by the server
                        if (!this._messages.IsEmpty) continue;
                        await this.HandleRequest(request);
                    }

                    // unset when a message/request is entered
                    await this._communicationHandle.WaitAsync();
                }
            }
            catch (Exception e)when (e is TaskCanceledException or OperationCanceledException
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
        });

        return thread;
    }

    private async Task<IMessage> WaitForMessage(CancellationToken ct = default)
    {
        if (this._messages.TryDequeue(out IMessage? immediate))
        {
            return immediate;
        }

        TaskCompletionSource<IMessage> tcs = new();
        EventHandler<MessageReceivedEventArgs> onReceived = (_, args) => { tcs.SetResult(args.Message); };

        ct.Register(() => tcs.TrySetCanceled());

        this.MessageReceived += onReceived;
        IMessage message = await tcs.Task;
        this.MessageReceived -= onReceived;
        return message;
    }

    private async Task SendMessage(IMessage message, CancellationToken ct = default)
    {
        if (this._secureClient?.Channel is null) return;

        try
        {
            MemoryStream stream = new();

            // skip the size, write out the message, then go back to write the real size
            // (means no extra copy)
            stream.Seek(sizeof(uint), SeekOrigin.Begin);
            FileTransferUtils.SendMessage(stream, message);
            uint size = (uint)stream.Length - sizeof(uint);

            stream.Seek(0, SeekOrigin.Begin);
            await StreamIO.WriteUInt32Async(stream, size, ct);
            
            await this._secureClient.Channel.SendAsync(stream.ToArray(), ct);
        }
        catch (Exception ex)
        {
            await this.ForceDisconnect(ex, FatalError.IOError);
            throw;
        }
    }

    private async Task ExpectAcknowledgement(MessageType type)
    {
        if (this._secureClient?.Channel is null)
        {
            await this._cancellationTokenSource.CancelAsync();
            throw new InvalidOperationException("cannot expect when disconnected");
        }

        try
        {
            IMessage message = await this.WaitForMessage();

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
            await this.ForceDisconnect(ex, FatalError.IOError);
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

    private void Disconnect()
    {
        this._secureClient?.Dispose();
        this._secureClient = null;
        this._cancellationTokenSource.Cancel();
        this._cancellationTokenSource.Dispose();
        this.IsConnected = false;
    }

    private void CloseAfterError(Exception ex) => this.CloseAfterError($"{ex.GetType()}: {ex.Message}");

    private void CloseAfterError(string msg)
    {
        this.Disconnect();
        this.ForceDisconnected?.Invoke(msg);
    }

    private async Task ForceDisconnect(Exception ex, FatalError errno) =>
        await this.ForceDisconnect($"{ex.GetType()}: {ex.Message}", errno);

    private async Task ForceDisconnect(string msg, FatalError errno)
    {
        if (this._secureClient?.Channel is not null) // true if connected + handshake completed before error
        {
            await this.SendMessage(new ErrorFatalMessage { Errno = errno });
        }

        this.CloseAfterError(msg);
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