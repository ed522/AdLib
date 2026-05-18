using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

using AdLib.IO;
using AdLib.IO.Messages;

namespace AdLib.Model;

public sealed partial class FileTransferServer
{
    private sealed class ClientHandler : IDisposable
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
        private bool _hasRunDisconnect;

        internal ClientHandler(
            ClientInfo info, SslStream sslStream, TcpClient insecureStream, string rootPath,
            CancellationToken cancellationToken
        )
        {
            this.Info = info;
            this._sslStream = sslStream;
            this._insecureStream = insecureStream;
            this._cancellationToken = cancellationToken;
            this._rootPath = rootPath;
        }

        public ClientInfo Info { get; }
        public string RemoteEndPoint => this.Info.RemoteEndPoint;

        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<FatalErrorOccurredEventArgs>? FatalErrorOccurred;
        public event EventHandler<RecoverableErrorOccurredEventArgs>? RecoverableErrorOccurred;
        public event EventHandler<FileReceivedEventArgs>? FileReceived;
        public event EventHandler<FileSentEventArgs>? FileSent;

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
                this._hasRunDisconnect = false;
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
                this.FatalErrorOccurred?.Invoke(this, new FatalErrorOccurredEventArgs
                {
                    Message = ex.Message,
                    CausingException = ex,
                });

                // separate variable for the event bc disconnection is only important for sending messages
                if (!Interlocked.CompareExchange(ref this._hasRunDisconnect, true, false))
                {
                    this.Disconnected?.Invoke(this, new DisconnectedEventArgs());
                }

                if (!this._isDisconnected)
                {
                    this._isDisconnected = true;
                    this.SendMessage(new ErrorFatalMessage { Errno = FatalError.Unspecified });
                }
            }
            finally
            {
                this._isDisconnected = true;
                
                if (!Interlocked.CompareExchange(ref this._hasRunDisconnect, true, false))
                {
                    this.Disconnected?.Invoke(this, new DisconnectedEventArgs());
                }

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
                    this.FileSent?.Invoke(this, new FileSentEventArgs { Path = request.Path });
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
                    this.FileReceived?.Invoke(this, new FileReceivedEventArgs { Path = finished.Path });
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
