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
    ///     only be
    ///     used for informational purposes.
    /// </summary>
    public event Action<string>? ClientDisconnected;

    /// <summary>
    ///     Called when a client fails to authenticate for some reason. The certificate might be null if
    ///     the
    ///     client fails to present one, in which case it is not possible to trust nor communicate with
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
        // single-threaded (one client only)
        Thread listenerThread = new(() =>
        {
            // keep accepting clients until the server is stopped
            while (!this._cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    TlsUtils.ConnectionInfo clientInfo = this._tlsServer.AcceptClient();
                    using SslStream? sslStream = clientInfo.SslStream;
                    using TcpClient? tcpClient = clientInfo.InsecureClient;

                    string host = clientInfo.Hostname;
                    TlsUtils.ConnectionResult result = clientInfo.Result;
                    TlsUtils.RejectionReason reason = clientInfo.Reason;

                    if (result != TlsUtils.ConnectionResult.Success ||
                        reason != TlsUtils.RejectionReason.None)
                    {
                        // subscribers will handle both remote rejections (reason) and
                        // local rejections (result)
                        this.AuthenticationError?.Invoke(host, clientInfo.Certificate, result, reason);
                        continue;
                    }

                    if (sslStream is null)
                    {
                        continue; // failed to connect
                    }

                    ClientHandler handler =
                        new(host, sslStream, this._cancellationTokenSource.Token, this._rootPath);

                    handler.Disconnected += h =>
                    {
                        lock (this._clientsLock)
                        {
                            this._clients.Remove(h);
                        }

                        this.ClientDisconnected?.Invoke(h.RemoteEndPoint);
                    };

                    lock (this._clientsLock)
                    {
                        this._clients.Add(handler);
                    }

                    this.ClientConnected?.Invoke(handler.RemoteEndPoint);
                    handler.Start();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Listener might have stopped or connection failed during handshake
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
        private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = new();
        private readonly CancellationToken _cancellationToken;
        private readonly Random _random = new();

        private readonly string _rootPath;
        private readonly SslStream _sslStream;
        private bool _isDisconnected;

        public ClientHandler(
            string remoteEndPoint, SslStream sslStream, CancellationToken cancellationToken, string rootPath
        )
        {
            this._rootPath = rootPath;
            this._sslStream = sslStream;
            this._cancellationToken = cancellationToken;
            this.RemoteEndPoint = remoteEndPoint;
        }

        public string RemoteEndPoint { get; }

        public void Dispose()
        {
            // do not dispose stream, it is owned by the server (which disposes it in a using statement)
            foreach ((string partPath, FileStream stream) download in this._activeDownloads.Values)
            {
                download.stream.Dispose();
            }

            this._activeDownloads.Clear();
        }

        public event Action<ClientHandler>? Disconnected;

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
                // Handshake
                // determines type
                int headerByte = this._sslStream.ReadByte();

                if (headerByte == -1)
                {
                    throw new EndOfStreamException("Stream unexpectedly closed before initialization");
                }

                MessageType type = (MessageType)headerByte;

                // NOTE: make sure that the switch is exhaustive
                InitMessage message1 = new();

                if (type != MessageType.Init)
                {
                    throw new InvalidDataException($"Invalid initialization: 0x{headerByte:X2}");
                }

                // this initializes all of the fields on the struct
                // no fields to check
                message1.Deserialize(this._sslStream);

                FileTransferUtils.SendMessage(this._sslStream, new InitAckMessage());

                while (!this._cancellationToken.IsCancellationRequested && !this._isDisconnected)
                {
                    this.HandleMessage(FileTransferUtils.ReadMessage(this._sslStream));
                    Thread.Sleep(CommsLoopDelayMs);
                    Thread.Yield();
                }
            }
            catch (Exception)
            {
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
