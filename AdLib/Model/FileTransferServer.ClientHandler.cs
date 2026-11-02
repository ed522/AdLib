using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
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

        private readonly HashSet<string> _activeDownloads = [];
        private readonly IFileSystemService _fileSystemService = new FileSystemService();
        private readonly string _rootPath;
        private readonly SemaphoreSlim _communicationHandle = new(0);

        private readonly CancellationToken _cancellationToken;

        private bool _isDisconnected;
        private bool _hasRunDisconnect;

        /// <summary>
        ///     Creates a handler around an already-established message channel, without performing any
        ///     connection setup. Useful if the caller owns the connection/channel (for example, in tests).
        /// </summary>
        internal ClientHandler(
            ClientInfo info, string rootPath, CancellationToken cancellationToken,
            IFileSystemService? fileSystemService = null
        ) : this(info, null, rootPath, fileSystemService, cancellationToken)
        {
            // empty
        }

        private ClientHandler(
            ClientInfo info, SecureConnection? connection, string rootPath,
            IFileSystemService? fileSystemService, CancellationToken cancellationToken
        )
        {
            this.Info = info;
            this._connection = connection;
            this._messageChannel = info.MessageChannel;
            this._cancellationToken = cancellationToken;
            this._rootPath = rootPath;

            if (fileSystemService is not null)
            {
                this._fileSystemService = fileSystemService;
            }
        }

        public ClientInfo Info { get; }
        public char ClientPathSeparator { get; private set; }

        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<FatalErrorOccurredEventArgs>? FatalErrorOccurred;
        public event EventHandler<RecoverableErrorOccurredEventArgs>? RecoverableErrorOccurred;
        public event EventHandler<TransferStartingEventArgs>? TransferStarting;
        public event EventHandler<TransferFinishedEventArgs>? TransferFinished;

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

            this._activeDownloads.Clear();
        }

        public Task Start()
        {
            var task = Task.Run(this.Run, this._cancellationToken);
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

        private async Task HandleMessage(IMessage message, CancellationToken ct = default)
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
            var block = await this._fileSystemService.ResendBlockAsync(resend.Path, resend.Offset, ct);

            await this.SendMessage(new DataMessage
            {
                Path = block.Path,
                Data = block.Data,
                Offset = block.Offset,
                Crc32 = block.Crc32,
            }, ct);
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
            this._fileSystemService.FinalizeDownload(finished.Path);
            this._activeDownloads.Remove(finished.Path);

            this.TransferFinished?.Invoke(this,
                new TransferFinishedEventArgs { Path = finished.Path, IsSending = false });
        }

        private async Task HandleDataMessage(DataMessage data, CancellationToken ct = default)
        {
            if (data.Path is null || data.Data is null)
            {
                throw new InvalidOperationException("Data message is missing path or data");
            }
            
            await this.CheckPath(data.Path, ct);

            if (this._activeDownloads.Add(data.Path))
            {
                this.TransferStarting?.Invoke(this,
                    new TransferStartingEventArgs { Path = data.Path, IsSending = false });
            }

            var resend = await this._fileSystemService
                                   .ProcessDownloadChunkAsync(data.Path, data.Data, data.Offset, data.Crc32, ct);

            if (resend is { } info)
            {
                await this.SendMessage(new ResendRequestMessage { Path = info.Path, Offset = info.Offset }, ct);
            }
        }

        private async Task HandleMakeDirMessage(MakeDirMessage makeDir, CancellationToken ct = default)
        {
            await this.CheckPath(makeDir.Path, ct);

            this._fileSystemService.CreateDirectory(makeDir.Path);
            await this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.MakeDir }, ct);
        }

        private async Task HandleDeleteMessage(DeleteMessage delete, CancellationToken ct = default)
        {
            await this.CheckPath(delete.Path, ct);

            this._fileSystemService.Delete(delete.Path);

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
            await this.CheckPath(request.RemotePath, ct);

            this.TransferStarting?.Invoke(this, new TransferStartingEventArgs
            {
                Path = request.RemotePath,
                IsSending = true,
            });

            var dataChannel =
                Channel.CreateBounded<IFileSystemService.DataBlock>(128);

            var directoryChannel =
                Channel.CreateBounded<IFileSystemService.PathInfo>(2048);

            var uploadTask = this._fileSystemService.UploadPathAsync(
                request.RemotePath, request.DestinationPath, dataChannel.Writer, directoryChannel.Writer, ct
            );

            var directoryTask = this.SendDirectoriesAsync(directoryChannel.Reader, ct);
            var dataTask = this.SendDataBlocksAsync(dataChannel.Reader, ct);

            try
            {
                await uploadTask;
            }
            finally
            {
                dataChannel.Writer.TryComplete();
                directoryChannel.Writer.TryComplete();

                await Task.WhenAll(directoryTask, dataTask);
            }

            this.TransferFinished?.Invoke(this, new TransferFinishedEventArgs
            {
                Path = request.RemotePath,
                IsSending = true,
            });
        }

        private async Task SendDirectoriesAsync(
            ChannelReader<IFileSystemService.PathInfo> reader, CancellationToken ct
        )
        {
            await foreach (var info in reader.ReadAllAsync(ct))
            {
                await this.SendMessage(new MakeDirMessage { Path = info.Path }, ct);
            }
        }

        private async Task SendDataBlocksAsync(
            ChannelReader<IFileSystemService.DataBlock> reader, CancellationToken ct
        )
        {
            await foreach (var block in reader.ReadAllAsync(ct))
            {
                await this.SendMessage(new DataMessage
                {
                    Path = block.Path,
                    Data = block.Data,
                    Offset = block.Offset,
                    Crc32 = block.Crc32,
                }, ct);

                if (block.IsLastBlock)
                {
                    await this.SendMessage(new DataFinishedMessage { Path = block.Path }, ct);
                }
            }
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

        private async Task SendMessage(IMessage message, CancellationToken ct = default)
        {
            try
            {
                await this._messageChannel.WriteMessageAsync(message, ct);
            }
            catch (Exception)
            {
                await this.SendMessage(new ErrorFatalMessage
                {
                    Errno = FatalError.IOError,
                }, this._cancellationToken);

                await this._messageChannel.DisposeAsync();
                this.Dispose();

                throw;
            }
        }
    }
}