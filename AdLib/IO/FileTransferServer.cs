using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using AdLib.Identities;
using AdLib.IO.Messages;

namespace AdLib.IO;

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

    public void Start(Identity identity, Dictionary<string, X509Certificate> trustedCerts, string sharedPath)
    {
        this._rootPath = Path.GetFullPath(sharedPath);

        if (!Directory.Exists(this._rootPath))
        {
            throw new InvalidOperationException($"Path {this._rootPath} does not exist");
        }

        // server setup
        this._tlsServer = new TlsServer(identity);

        foreach ((string host, X509Certificate cert) in trustedCerts)
        {
            this._tlsServer.TrustCertificate(host, cert);
        }

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
                    TlsUtils.ConnectionInfo clientInfo = this._tlsServer.AcceptClient();
                    // client disposes streams in its Dispose, and that's called on thread exit
                    SslStream? sslStream = clientInfo.SslStream;
                    TcpClient? insecureStream = clientInfo.InsecureClient;

                    string host = clientInfo.Hostname;
                    TlsUtils.ConnectionResult result = clientInfo.Result;
                    TlsUtils.RejectionReason reason = clientInfo.Reason;

                    // did the authentication fail?
                    // ConnectionResult holds the local auth result (against remote host), RejectionReason is
                    // for remote auth (remote host checking our cert)
                    if (result != TlsUtils.ConnectionResult.Success ||
                        reason != TlsUtils.RejectionReason.None)
                    {
                        // subscribers will handle both remote rejections (reason) and
                        // local rejections (result)
                        this.AuthenticationError?.Invoke(host, clientInfo.Certificate, result, reason);
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

        /// <summary>
        ///     UNENCRYPTED TCP, do not use for anything except disposal
        /// </summary>
        private readonly TcpClient _insecureStream;

        private readonly SslStream _sslStream;
        private readonly CancellationToken _cancellationToken;
        private readonly string _rootPath;

        private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = [];
        private readonly Random _random = new();
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
                    this.SendMessage(new ErrorFatalMessage());
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
                    if (!CheckPath(this._rootPath, request.Path)) break;
                    FileTransferUtils.UploadPath(request.Path, request.Path, this.SendMessage);
                    break;

                case ListFilesMessage list:
                    if (!CheckPath(this._rootPath, list.Path)) break;
                    this.SendListing(list.Path);
                    break;

                case DeleteMessage delete:
                    if (!CheckPath(this._rootPath, delete.Path)) break;

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
                    if (!CheckPath(this._rootPath, makeDir.Path)) break;

                    Directory.CreateDirectory(makeDir.Path);
                    this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.MakeDir });
                    break;

                case DataMessage data:
                    if (!CheckPath(this._rootPath, data.Path)) break;
                    FileTransferUtils.ProcessDownloadChunk(data, this._activeDownloads, this._random);
                    break;

                case DataFinishedMessage finished:
                    if (!CheckPath(this._rootPath, finished.Path)) break;
                    FileTransferUtils.FinalizeDownload(finished.Path, this._activeDownloads);
                    break;

                case EndMessage:
                    this.SendMessage(new EndAckMessage());
                    this._isDisconnected = true;
                    break;

                case StatusRequestMessage status:
                    this.SendMessage(new StatusResponseMessage { Random = status.Random });
                    break;

                default:
                    throw new IOException($"Did not expect message of type {message.GetType()} at this " +
                                          $"point");
            }
        }

        /// <summary>
        ///     Verifies that the specified path is a legal path to request. Legal paths are those that are
        ///     subpaths of or equal to the root path.
        /// </summary>
        /// <param name="rootPath">the root path to check against</param>
        /// <param name="path">the path in question that should be checked</param>
        /// <returns><c>true</c> if the checked path is a subpath of or </returns>
        private static bool CheckPath(string rootPath, string path) => Path.GetFullPath(path)
                                                                           .StartsWith(rootPath,
                                                                               StringComparison
                                                                                   .OrdinalIgnoreCase);

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
