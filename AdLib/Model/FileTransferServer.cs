using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using AdLib.Identities;
using AdLib.IO;

namespace AdLib.Model;

public sealed class FileTransferServer : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<ClientHandler> _clients = [];
    private readonly Lock _clientsLock = new();
    private string _rootPath = "";
    private TlsServer? _tlsServer;

    public void Dispose()
    {
        this.Stop();
        this._tlsServer?.Dispose();
    }

    /// <summary>
    ///     Called when a client connects to the server. The client's IP is a parameter, but should only be
    ///     used for informational purposes.
    /// </summary>
    public event Action<string>? ClientConnected;

    /// <summary>
    ///     Called when a client disconnects from the server. The client's IP is a parameter, but should
    ///     only be used for informational purposes.
    /// </summary>
    public event Action<string>? ClientDisconnected;

    /// <summary>
    ///     Called when an error occurs in the server's listening thread.
    /// </summary>
    public event Action<Exception>? Error;

    /// <summary>
    ///     Called when a client fails to authenticate for some reason. The certificate might be null if
    ///     the client fails to present one, in which case it is not possible to trust nor communicate with
    ///     them.
    /// </summary>
    public event TlsUtils.AuthenticationErrorHandler? AuthenticationError;

    public void Start(Identity identity, TrustStore store, string sharedPath)
    {
        this._rootPath = Path.GetFullPath(sharedPath);

        if (!Directory.Exists(this._rootPath))
        {
            throw new InvalidOperationException($"Path {this._rootPath} does not exist");
        }

        // server setup
        this._tlsServer = new TlsServer(identity, store);
        this._tlsServer.Start();

        // define + start thread - since we hold a cancellation token we don't need to worry about keeping
        // references to stop it
        // ClientHandler is threaded so this can handle multiple at once
        Thread listenerThread = new(() =>
        {
            // keep accepting clients until the server is stopped
            while (!this._cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    TlsUtils.ConnectionInfo connectionInfo = this._tlsServer.AcceptClient();
                    // client disposes streams in its Dispose, and that's called on thread exit
                    SslStream? sslStream = connectionInfo.SslStream;
                    TcpClient? insecureStream = connectionInfo.InsecureClient;

                    string host = connectionInfo.Hostname;
                    Certificate? cert = connectionInfo.Certificate;
                    X509Certificate? presentedCert = connectionInfo.PresentedCert;
                    TlsUtils.ConnectionResult result = connectionInfo.Result;
                    TlsUtils.RejectionReason reason = connectionInfo.Reason;

                    // did the authentication fail?
                    // ConnectionResult holds the local auth result (against remote host), RejectionReason is
                    // for remote auth (remote host checking our cert)

                    if (result != TlsUtils.ConnectionResult.Success ||
                        reason != TlsUtils.RejectionReason.None)
                    {
                        // subscribers will handle both remote rejections (reason) and
                        // local rejections (result)
                        this.AuthenticationError?.Invoke(host, cert, presentedCert, result, reason);
                        continue;
                    }

                    if (sslStream is null || insecureStream is null)
                    {
                        continue; // failed to connect
                    }

                    ClientHandler handler =
                        new(host, sslStream, insecureStream,
                            this._cancellationTokenSource.Token, this._rootPath);

                    // get rid of client on disconnect
                    handler.Disconnected += h =>
                    {
                        lock (this._clientsLock)
                        {
                            this._clients.Remove(h);
                        }

                        this.ClientDisconnected?.Invoke(h.RemoteEndPoint);
                    };

                    handler.Error += ex => this.Error?.Invoke(ex);

                    lock (this._clientsLock)
                    {
                        this._clients.Add(handler);
                    }

                    this.ClientConnected?.Invoke(handler.RemoteEndPoint);
                    // client's partly connected so let the handler do its thing
                    handler.Start();
                }
                catch (Exception ex) when (ex is OperationCanceledException)
                {
                    // kill thread - this means the entire server was killed
                    return;
                }
            }
        })
        {
            IsBackground = true,
        };

        listenerThread.Start();
    }

    public void Stop()
    {
        this._cancellationTokenSource.Cancel();
        this._tlsServer?.Stop();

        lock (this._clientsLock)
        {
            foreach (ClientHandler client in this._clients)
            {
                client.Dispose();
            }

            this._clients.Clear();
        }
    }

    private class ClientHandler : IDisposable
    {
        private const int CommsLoopDelayMs = 10;

        private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = [];
        private readonly CancellationToken _cancellationToken;

        /// <summary>
        ///     UNENCRYPTED TCP, do not use for anything except disposal
        /// </summary>
        private readonly TcpClient _insecureStream;

        private readonly Random _random = new();
        private readonly string _rootPath;

        private readonly SslStream _sslStream;
        private bool _isDisconnected;

        public ClientHandler(
            string remoteEndPoint, SslStream sslStream, TcpClient insecureStream,
            CancellationToken cancellationToken, string rootPath
        )
        {
            this.RemoteEndPoint = remoteEndPoint;
            this._sslStream = sslStream;
            this._insecureStream = insecureStream;
            this._cancellationToken = cancellationToken;
            this._rootPath = rootPath;
        }

        public string RemoteEndPoint { get; }

        public void Dispose()
        {
            this._insecureStream.Dispose();
            this._sslStream.Dispose();

            foreach ((string partPath, FileStream stream) in this._activeDownloads.Values)
            {
                stream.Dispose();
                if (File.Exists(partPath)) File.Delete(partPath);
            }

            this._activeDownloads.Clear();
        }

        public event Action<ClientHandler>? Disconnected;
        public event Action<Exception>? Error;

        public void Start()
        {
            Thread thread = new(this.Run)
            {
                IsBackground = true,
            };

            thread.Start();
        }

        private void Run()
        {
            try
            {
                // determines type
                int headerByte = this._sslStream.ReadByte();

                if (headerByte == -1)
                {
                    throw new EndOfStreamException("Stream unexpectedly closed before initialization");
                }

                MessageType type = (MessageType)headerByte;

                if (type != MessageType.Init)
                {
                    throw new InvalidDataException($"Invalid initialization: 0x{headerByte:X2}");
                }

                InitMessage message1 = new();
                message1.Deserialize(this._sslStream);
                // no fields to check

                // acknowledge
                FileTransferUtils.SendMessage(this._sslStream, new InitAckMessage());

                while (!this._cancellationToken.IsCancellationRequested && !this._isDisconnected)
                {
                    // the server never sends its own messages except as a consequence of a client's message
                    this.HandleMessage(FileTransferUtils.ReadMessage(this._sslStream));
                    Thread.Sleep(CommsLoopDelayMs);
                    Thread.Yield();
                }
            }
            catch (Exception ex)
            {
                this.Error?.Invoke(ex);

                if (!this._isDisconnected)
                {
                    this.SendMessage(new ErrorFatalMessage { Errno = FatalError.Unspecified });
                }
            }
            finally
            {
                this.Disconnected?.Invoke(this);
                this.Dispose();
            }
        }

        private void HandleMessage(IMessage message)
        {
            switch (message)
            {
                case FileRequestMessage request:
                    this.CheckPath(request.Path);
                    FileTransferUtils.UploadPath(request.Path, request.Path, this.SendMessage);
                    break;

                case ListFilesMessage list:
                    this.CheckPath(list.Path);
                    this.SendListing(list.Path);
                    break;

                case DeleteMessage delete:
                    this.CheckPath(delete.Path);

                    if (File.Exists(delete.Path))
                    {
                        File.Delete(delete.Path);
                    }
                    else if (Directory.Exists(delete.Path))
                    {
                        Directory.Delete(delete.Path, true);
                    }

                    this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.Delete });
                    break;

                case MakeDirMessage makeDir:
                    this.CheckPath(makeDir.Path);

                    FileTransferUtils.CreateDirectory(makeDir.Path);
                    this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.MakeDir });
                    break;

                case DataMessage data:
                    this.CheckPath(data.Path);

                    FileTransferUtils.ProcessDownloadChunk(data, this._activeDownloads, this._random,
                        this.SendMessage);

                    break;

                case DataFinishedMessage finished:
                    this.CheckPath(finished.Path);
                    FileTransferUtils.FinalizeDownload(finished.Path, this._activeDownloads);
                    break;

                case EndMessage:
                    this.SendMessage(new EndAckMessage());
                    this._isDisconnected = true;
                    break;

                case StatusRequestMessage status:
                    this.SendMessage(new StatusResponseMessage { Random = status.Random });
                    break;

                case ResendRequestMessage resend:
                    this.CheckPath(resend.Path);
                    FileTransferUtils.ResendBlock(resend, this.SendMessage);
                    break;

                default:
                    throw new CommunicationsException($"Did not expect message of type " +
                                                      $"{message.GetType()} at this point");
            }
        }

        /// <summary>
        ///     Verifies that the specified path is a legal path to request. Legal paths are those that are
        ///     subpaths of or equal to the root path.
        /// </summary>
        /// <param name="path">the path that should be checked against this server's root path</param>
        /// <returns><c>true</c> if the checked path is a subpath of or </returns>
        private void CheckPath(string path)
        {
            bool isValid = false;

            DirectoryInfo baseInfo = new(Path.GetFullPath(this._rootPath));
            DirectoryInfo? dirInfo = new(Path.GetFullPath(path));

            while (dirInfo != null)
            {
                // case-neutrally check if two paths are equal (relative distance is 0)
                // may not work in case of soft/hard links but that would only affect out-of-scope links that
                // link to somewhere in-scope, which is sufficiently uncommon to reject
                if (Path.GetRelativePath(baseInfo.Name, dirInfo.Name) == ".")
                {
                    isValid = true;
                    break;
                }

                // go up the chain until the root
                dirInfo = dirInfo.Parent;
            }

            if (!isValid)
            {
                this.SendMessage(new ErrorRecoverableMessage { Errno = RecoverableError.PathOutOfScope });
            }
        }

        private void SendMessage(IMessage message)
        {
            FileTransferUtils.SendMessage(this._sslStream, message);
        }

        private void SendListing(string path)
        {
            if (!Directory.Exists(path))
            {
                this.SendMessage(new ListFilesResponseMessage { Path = path, Files = [] });
                return;
            }

            string[] directories = Directory.GetDirectories(path);
            string[] files = Directory.GetFiles(path);
            FileEntry[] entries = new FileEntry[directories.Length + files.Length];

            for (int i = 0; i < directories.Length; i++)
            {
                entries[i] = new FileEntry { Name = Path.GetFileName(directories[i]), IsDirectory = true };
            }

            for (int i = 0; i < files.Length; i++)
            {
                entries[directories.Length + i] = new FileEntry
                    { Name = Path.GetFileName(files[i]), IsDirectory = false };
            }

            this.SendMessage(new ListFilesResponseMessage { Path = path, Files = entries });
        }
    }
}
