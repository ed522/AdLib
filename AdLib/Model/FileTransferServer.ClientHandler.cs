using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AdLib.IO;
using AdLib.IO.Messages;

namespace AdLib.Model;

public sealed partial class FileTransferServer
{
    internal sealed class ClientHandler : IDisposable
    {
        private readonly SecureConnection? _connection;
        private readonly IMessageChannel _messageChannel;

        private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = [];
        private readonly string _rootPath;
        private readonly Random _random = new();
        private readonly SemaphoreSlim _communicationHandle = new(0);

        private readonly CancellationToken _cancellationToken;

        private bool _isDisconnected;
        private bool _hasRunDisconnect;

        internal ClientHandler(
            ClientInfo info, SecureConnection connection, string rootPath,
            CancellationToken cancellationToken
        ) : this(info, CreateMessageChannel(connection), connection, rootPath, cancellationToken)
        {
            // empty
        }

        /// <summary>
        ///     Creates a handler around an already-established message channel, without performing any
        ///     connection setup. Useful if the caller owns the connection/channel (for example, in tests).
        /// </summary>
        internal ClientHandler(
            ClientInfo info, IMessageChannel messageChannel, string rootPath,
            CancellationToken cancellationToken
        ) : this(info, messageChannel, null, rootPath, cancellationToken)
        {
            // empty
        }

        private ClientHandler(
            ClientInfo info, IMessageChannel messageChannel, SecureConnection? connection, string rootPath,
            CancellationToken cancellationToken
        )
        {
            this.Info = info;
            this._connection = connection;
            this._messageChannel = messageChannel;
            this._cancellationToken = cancellationToken;
            this._rootPath = rootPath;
        }

        private static IMessageChannel CreateMessageChannel(SecureConnection connection)
        {
            IDataBlockChannel blockChannel = new SshDataBlockChannel(connection.Channel);
            return new BlockMessageChannel(blockChannel);
        }

        public ClientInfo Info { get; }
        public char ClientPathSeparator { get; private set; }

        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<FatalErrorOccurredEventArgs>? FatalErrorOccurred;
        public event EventHandler<RecoverableErrorOccurredEventArgs>? RecoverableErrorOccurred;
        public event EventHandler<TransferStartingEventArgs>? TransferStarting;
        public event EventHandler<TransferFinishedEventArgs>? TransferFinished;

        private class MessageReceivedEventArgs : EventArgs
        {
            public required IMessage Message { get; init; }
        }

        public void Dispose()
        {
            if (this._connection is not null)
            {
                this._connection.Dispose();
            }
            else
            {
                this._messageChannel.Dispose();
            }

            foreach ((string partPath, FileStream stream) in this._activeDownloads.Values)
            {
                stream.Dispose();
                if (File.Exists(partPath)) File.Delete(partPath);
            }

            this._activeDownloads.Clear();
        }

        public Task Start(CancellationToken ct = default)
        {
            Task task = Task.Run(this.Run, ct);
            return task;
        }

        private async Task Run()
        {
            try
            {
                // allows thread to continue accepting messages once one comes in
                this._messageChannel.MessageReceived += (_, _) => this._communicationHandle.Release();

                await this.ProcessHandshake();

                while (!this._cancellationToken.IsCancellationRequested && !this._isDisconnected)
                {
                    // the server never sends its own messages except as a consequence of a client's message
                    while (this._messageChannel.TryReadMessageIfAvailable(out IMessage? message))
                    {
                        await this.HandleMessage(message, this._cancellationToken);
                    }

                    // released on message received
                    await this._communicationHandle.WaitAsync(this._cancellationToken);
                }
            }
            catch (Exception ex)
            {
                this.FatalErrorOccurred?.Invoke(this, new FatalErrorOccurredEventArgs
                {
                    Message = ex.Message,
                    CausingException = ex,
                });

                // _isDisconnected is for messages, _hasRunDisconnect is for the event invocation
                if (!this._isDisconnected)
                {
                    this._isDisconnected = true;

                    await this.SendMessage(new ErrorFatalMessage
                    {
                        Errno = FatalError.Unspecified,
                    }, this._cancellationToken);
                }
            }
            finally
            {
                if (!Interlocked.CompareExchange(ref this._hasRunDisconnect, true, false))
                {
                    this.Disconnected?.Invoke(this, new DisconnectedEventArgs());
                }

                this.Dispose();
            }
        }

        private async Task ProcessHandshake()
        {
            IMessage msg = await this._messageChannel.ReadMessageAsync(this._cancellationToken);

            if (msg is not InitMessage)
            {
                throw new InvalidOperationException($"Expected InitMessage, got {msg.Header}");
            }

            // acknowledge
            await this.SendMessage(
                new InitAckMessage
                {
                    SharedFolderPath = this._rootPath,
                },
                this._cancellationToken
            );
        }

        internal async Task HandleMessage(IMessage message, CancellationToken ct = default)
        {
            switch (message)
            {
                case FileRequestMessage request:
                    await this.HandleFileRequestMessage(request, ct);
                    break;

                case ListFilesMessage list:
                    await this.HandleListFilesMessage(list, ct);
                    break;

                case DeleteMessage delete:
                    await this.HandleDeleteMessage(delete, ct);
                    break;

                case MakeDirMessage makeDir:
                    await this.HandleMakeDirMessage(makeDir, ct);
                    break;

                case DataMessage data:
                    await this.HandleDataMessage(data, ct);
                    break;

                case DataFinishedMessage finished:
                    await this.HandleDataFinishedMessage(finished, ct);
                    break;

                case EndMessage:
                    await this.HandleEndMessage(ct);
                    break;

                case StatusRequestMessage status:
                    await this.HandleStatusRequestMessage(status, ct);
                    break;

                case ResendRequestMessage resend:
                    await this.HandleResendRequestMessage(resend, ct);
                    break;

                default:
                    throw new CommunicationsException($"Did not expect message of type " +
                                                      $"{message.GetType()} at this point");
            }
        }

        private async Task HandleResendRequestMessage(ResendRequestMessage resend, CancellationToken ct = default)
        {
            await this.CheckPath(resend.Path, ct);
            await FileTransferUtils.ResendBlockAsync(resend, this.SendMessage, ct);
        }

        private async Task HandleStatusRequestMessage(StatusRequestMessage status, CancellationToken ct = default)
        {
            await this.SendMessage(new StatusResponseMessage { Random = status.Random }, ct);
        }

        private async Task HandleEndMessage(CancellationToken ct)
        {
            await this.SendMessage(new EndAckMessage(), ct);
            this._isDisconnected = true;
        }

        private async Task HandleDataFinishedMessage(DataFinishedMessage finished, CancellationToken ct = default)
        {
            await this.CheckPath(finished.Path, ct);
            FileTransferUtils.FinalizeDownload(finished.Path, this._activeDownloads);

            this.TransferFinished?.Invoke(this,
                new TransferFinishedEventArgs { Path = finished.Path, IsSending = false });
        }

        private async Task HandleDataMessage(DataMessage data, CancellationToken ct = default)
        {
            await this.CheckPath(data.Path, ct);

            if (!this._activeDownloads.ContainsKey(data.Path))
            {
                this.TransferStarting?.Invoke(this,
                    new TransferStartingEventArgs { Path = data.Path, IsSending = false });
            }

            await FileTransferUtils.ProcessDownloadChunkAsync(data,
                this._activeDownloads,
                this._random,
                this.SendMessage,
                ct
            );
        }

        private async Task HandleMakeDirMessage(MakeDirMessage makeDir, CancellationToken ct = default)
        {
            await this.CheckPath(makeDir.Path, ct);

            FileTransferUtils.CreateDirectory(makeDir.Path);
            await this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.MakeDir }, ct);
        }

        private async Task HandleDeleteMessage(DeleteMessage delete, CancellationToken ct = default)
        {
            await this.CheckPath(delete.Path, ct);

            if (File.Exists(delete.Path))
            {
                File.Delete(delete.Path);
            }
            else if (Directory.Exists(delete.Path))
            {
                Directory.Delete(delete.Path, true);
            }

            await this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.Delete }, ct);
        }

        private async Task HandleListFilesMessage(ListFilesMessage list, CancellationToken ct = default)
        {
            await this.CheckPath(list.Path, ct);

            if (!Directory.Exists(list.Path))
            {
                await this.SendMessage(new ErrorRecoverableMessage
                {
                    Errno = RecoverableError.ERRNO_NO_SUCH_FILE_OR_DIRECTORY,
                }, ct);
            }
            else
            {
                string[] directories = Directory.GetDirectories(list.Path);
                string[] files = Directory.GetFiles(list.Path);
                FileEntry[] entries = new FileEntry[directories.Length + files.Length];

                for (int i = 0; i < directories.Length; i++)
                {
                    entries[i] = new FileEntry
                    {
                        Name = Path.GetFileName(directories[i]),
                        IsDirectory = true,
                    };
                }

                for (int i = 0; i < files.Length; i++)
                {
                    entries[directories.Length + i] = new FileEntry
                    {
                        Name = Path.GetFileName(files[i]),
                        IsDirectory = false,
                    };
                }

                await this.SendMessage(new ListFilesResponseMessage { Path = list.Path, Files = entries }, ct);
            }
        }

        private async Task HandleFileRequestMessage(FileRequestMessage request, CancellationToken ct = default)
        {
            await this.CheckPath(request.Path, ct);

            this.TransferStarting?.Invoke(this, new TransferStartingEventArgs
            {
                Path = request.Path,
                IsSending = true,
            });

            await FileTransferUtils.UploadPathAsync(request.Path, request.Path, this.SendMessage, ct);

            this.TransferFinished?.Invoke(this, new TransferFinishedEventArgs
            {
                Path = request.Path,
                IsSending = true,
            });
        }

        /// <summary>
        ///     Verifies that the specified path is a legal path to request. Legal paths are those that are
        ///     subpaths of or equal to the root path. In the case of an invalid path, an error message is sent to the
        ///     remote host.
        /// </summary>
        /// <param name="path">the path that should be checked against this server's root path</param>
        /// <param name="ct">the cancellation token to cancel any send operation invoked by this function, if
        /// applicable</param>
        /// <returns><c>true</c> if the checked path is a subpath of or </returns>
        private async Task CheckPath(string path, CancellationToken ct = default)
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
                await this.SendMessage(new ErrorRecoverableMessage { Errno = RecoverableError.PathOutOfScope }, ct);
            }
        }

        internal async Task SendMessage(IMessage message, CancellationToken ct = default)
        {
            try
            {
                await this._messageChannel.WriteMessageAsync(message, ct);
            }
            catch (Exception ex)
            {
                await this.SendMessage(new ErrorFatalMessage
                {
                    Errno = FatalError.IOError,
                }, this._cancellationToken);

                await this._messageChannel.DisposeAsync();

                throw;
            }
        }
    }
}