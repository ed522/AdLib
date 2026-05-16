using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using AdLib.Identities;
using AdLib.IO;
using AdLib.IO.Messages;

namespace AdLib.Model;

public sealed class FileTransferClient : IDisposable
{
    public delegate void FileListingHandler(string path, FileEntry[] files);
    private const int CommsLoopDelayMs = 10;
    private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Random _random = new();
    private readonly ConcurrentQueue<ClientRequest> _requests = new();

    private string _latestDownload = "";
    private TlsClient? _tlsClient;
    public bool IsConnected { get; private set; }

    public void Dispose()
    {
        this._tlsClient?.Dispose();

        foreach ((string partPath, FileStream stream) download in this._activeDownloads.Values)
        {
            download.stream.Dispose();
        }

        this._activeDownloads.Clear();
    }

    public event FileListingHandler? FileListingReceived;
    public event TlsUtils.AuthenticationErrorHandler? CertificateError;
    public event Action<string>? GracefullyDisconnected;
    public event Action<string>? ForceDisconnected;
    public event Action<string>? RecoverableError;

    public event Action<string>? Connected;
    public event Action<string>? FileSending;
    public event Action<string>? FileReceiving;

    public void AddRequest(ClientRequest request) { this._requests.Enqueue(request); }

    public void ConnectAndListen(string host, Identity identity)
    {
        Thread thread = new(() =>
        {
            try
            {
                this._tlsClient = new TlsClient();
                TlsUtils.ConnectionInfo info = this._tlsClient.Connect(host, identity);
                TlsUtils.ConnectionResult result = info.Result;
                TlsUtils.RejectionReason reason = info.Reason;

                // sanity checks
                // NOTE: do not use this.ForceDisconnect() here, it will attempt to send an error message
                // but we aren't connected until the event is fired
                if (this._tlsClient.SslStream is null)
                {
                    InvalidOperationException ex = new("TLS stream is null after creation");
                    this.CloseAfterError(ex);
                    throw ex;
                }

                // lets implementation pop up an error box, or show the fingerprint to help in adding a new
                // cert
                if (result != TlsUtils.ConnectionResult.Success || reason != TlsUtils.RejectionReason.None)
                {
                    if (info.Certificate is null)
                    {
                        IOException ex = new("Remote host did not offer a certificate");
                        this.CloseAfterError(ex);
                        throw ex;
                    }

                    // handles adding trust, showing fingerprint, error dialogues etc.
                    this.CertificateError?.Invoke(host, info.Certificate, result, reason);
                    return;
                }

                this.IsConnected = true;
                // do handshake
                this.SendMessage(new InitMessage());
                InitAckMessage acknowledgement = new();

                // manually check that the init acknowledgement is correct
                int type = this._tlsClient.SslStream.ReadByte();

                if (type == -1)
                {
                    IOException ex = new("Stream closed before initialization was completed");
                    this.CloseAfterError(ex);
                    throw ex;
                }

                // ...
                if ((MessageType)type != MessageType.InitAck)
                {
                    IOException ex = new($"Invalid initialization response: 0x{type:X2}");
                    this.CloseAfterError(ex);
                    throw ex;
                }

                // no fields to check
                acknowledgement.Deserialize(this._tlsClient.SslStream);

                // this is the point where we are "connected" (successfully connected to a trusted &
                // funcitoning host)
                this.Connected?.Invoke(host);

                // comms loop
                while (!this._cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // safety - all disconnections should also cancel
                    if (!this.IsConnected)
                    {
                        this._cancellationTokenSource.Cancel();
                    }

                    this.CommunicateLoop(this._tlsClient);
                    Thread.Sleep(CommsLoopDelayMs); // avoid busy loop
                    Thread.Yield();
                }

                // cancelled
            }
            finally
            {
                this.Disconnect(); // does not fire events nor send messages, also frees managed resources
            }
        });

        thread.Start();
    }

    private void CommunicateLoop(TlsClient tlsClient)
    {
        if (tlsClient.SslStream == null) return;

        try
        {
            // client-side requests
            while (this._requests.TryDequeue(out ClientRequest request))
            {
                this.HandleRequest(request);
            }

            // server messages
            while (tlsClient.HasData)
            {
                IMessage message = FileTransferUtils.ReadMessage(tlsClient.SslStream);
                this.HandleServerMessage(message);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidDataException)
        {
            this.ForceDisconnect(ex, FatalError.IOError);
        }
        catch (Exception ex) when (ex is not SystemException)
        {
            this.ForceDisconnect(ex, FatalError.Unspecified);
        }
    }

    private void SendMessage(IMessage message)
    {
        if (this._tlsClient?.SslStream == null) return;

        try
        {
            FileTransferUtils.SendMessage(this._tlsClient.SslStream, message);
        }
        catch (Exception ex)
        {
            this.ForceDisconnect(ex, FatalError.IOError);
            throw;
        }
    }

    private void ExpectAcknowledgement(MessageType type)
    {
        if (this._tlsClient?.SslStream == null)
        {
            this._cancellationTokenSource.Cancel();
            throw new InvalidOperationException("cannot expect when disconnected");
        }

        ControlAckMessage message = new();

        try
        {
            message.Deserialize(this._tlsClient.SslStream);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            this.ForceDisconnect(ex, FatalError.IOError);
            throw;
        }

        if (message.ControlCode != (byte)type)
        {
            InvalidOperationException ex = new($"Expected {type} acknowledgement, got {message.ControlCode}");
            this.ForceDisconnect(ex, FatalError.InvalidAcknowledgement);
            throw ex;
        }
    }

    private void HandleRequest(ClientRequest request)
    {
        switch (request.Type)
        {
            case ClientRequestType.PutPath:
                this.UploadPath(request.Path, Path.GetFileName(request.Path));
                break;

            case ClientRequestType.GetPath:
                this.SendMessage(new FileRequestMessage { Path = request.Path });
                break;

            case ClientRequestType.DeleteRemotePath:
                this.SendMessage(new DeleteMessage { Path = request.Path });
                this.ExpectAcknowledgement(MessageType.Delete);
                break;

            case ClientRequestType.MakeRemoteDir:
                this.SendMessage(new MakeDirMessage { Path = request.Path });
                this.ExpectAcknowledgement(MessageType.MakeDir);
                break;

            case ClientRequestType.ListFiles:
                this.SendMessage(new ListFilesMessage { Path = request.Path });
                break;

            case ClientRequestType.Disconnect:
                this.SendMessage(new EndMessage());
                break;
        }
    }

    private void HandleServerMessage(IMessage message)
    {
        switch (message)
        {
            case InitAckMessage:
                break;

            case ErrorFatalMessage fatal:
                this._cancellationTokenSource.Cancel();
                this.IsConnected = false;
                this.ForceDisconnected?.Invoke($"Fatal server error (error code 0x{fatal.Errno:X8})");
                break;

            case ErrorRecoverableMessage recoverable:
                this.RecoverableError?.Invoke($"{recoverable.Errno}");
                break;

            case EndMessage:
                this.SendMessage(new EndAckMessage());
                this.GracefullyDisconnected?.Invoke("Disconnected by server");
                this._cancellationTokenSource.Cancel();
                this.IsConnected = false;
                break;

            case EndAckMessage:
                this.GracefullyDisconnected?.Invoke("Disconnected");
                this.IsConnected = false;
                break;

            case ListFilesResponseMessage listFiles:
                this.FileListingReceived?.Invoke(listFiles.Path, listFiles.Files);
                break;

            case DataMessage data:
                this.HandleIncomingData(data);
                break;

            case DataFinishedMessage finished:
                this.FinalizeDownload(finished.Path);
                break;

            case MakeDirMessage makeDir:
                FileTransferUtils.CreateDirectory(makeDir.Path);
                break;

            case StatusRequestMessage status: // not used by server
                this.SendMessage(new StatusResponseMessage { Random = status.Random });
                break;

            case ResendRequestMessage resend:
                FileTransferUtils.ResendBlock(resend, this.SendMessage);
                break;

            case ControlAckMessage ack: // will manually be expected
                throw new InvalidOperationException($"Unexpected acknowledgement for {ack.ControlCode}");

            default:
                throw new InvalidOperationException($"Unknown message type: {message.GetType()}");
        }
    }

    private void Disconnect()
    {
        this._tlsClient?.Dispose();
        this._tlsClient = null;
        this._cancellationTokenSource.Cancel();
        this.IsConnected = false;
    }

    private void CloseAfterError(Exception ex) => this.CloseAfterError($"{ex.GetType()}: {ex.Message}");

    private void CloseAfterError(string msg)
    {
        this.Disconnect();
        this.ForceDisconnected?.Invoke(msg);
    }

    private void ForceDisconnect(Exception ex, FatalError errno) => 
        this.ForceDisconnect($"{ex.GetType()}: {ex.Message}", errno);

    private void ForceDisconnect(string msg, FatalError errno)
    {
        if (this._tlsClient?.SslStream is not null) // true if connected + handshake completed before error
        {
            this.SendMessage(new ErrorFatalMessage { Errno = errno });
        }

        this.CloseAfterError(msg);
    }

    private void HandleIncomingData(DataMessage data)
    {
        if (this._latestDownload != data.Path)
        {
            this._latestDownload = data.Path;
            this.FileReceiving?.Invoke(data.Path);
        }

        FileTransferUtils.ProcessDownloadChunk(data, this._activeDownloads, this._random, this.SendMessage);
    }

    private void FinalizeDownload(string path)
    {
        FileTransferUtils.FinalizeDownload(path, this._activeDownloads);
    }

    private void UploadPath(string localPath, string remotePath)
    {
        this.FileSending?.Invoke(localPath);
        FileTransferUtils.UploadPath(localPath, remotePath, this.SendMessage);
    }
}
