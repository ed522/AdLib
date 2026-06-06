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
    private sealed class ClientHandler : IDisposable
    {
        private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = [];
        private readonly SecureConnection _connection;
        private readonly string _rootPath;
        private readonly Random _random = new();
        private readonly SemaphoreSlim _communicationHandle = new(0);
        private readonly Queue<IMessage> _messages = new();
        private uint _currentExpectedSize;
        private readonly MemoryStream _partialBuffer = new();

        private readonly CancellationToken _cancellationToken;

        private bool _isDisconnected;
        private bool _hasRunDisconnect;

        internal ClientHandler(
            ClientInfo info, SecureConnection connection, string rootPath,
            CancellationToken cancellationToken
        )
        {
            this.Info = info;
            this._connection = connection;
            this._cancellationToken = cancellationToken;
            this._rootPath = rootPath;
        }

        public ClientInfo Info { get; }

        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<FatalErrorOccurredEventArgs>? FatalErrorOccurred;
        public event EventHandler<RecoverableErrorOccurredEventArgs>? RecoverableErrorOccurred;
        public event EventHandler<TransferStartingEventArgs>? TransferStarting;
        public event EventHandler<TransferFinishedEventArgs>? TransferFinished;

        private class MessageReceivedEventArgs : EventArgs
        {
            public required IMessage Message { get; init; }
        }

        private event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        public void Dispose()
        {
            this._connection.Dispose();

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
                this._connection.Channel.DataReceived += (_, args) =>
                {
                    this._currentExpectedSize = FileTransferUtils.BufferMessage(
                        this._currentExpectedSize,
                        args.Array,
                        this._partialBuffer,
                        out IMessage? message
                    );

                    this._connection.Channel.AdjustWindow((uint)args.Array.Length);

                    // not null on complete message, null while buffering
                    if (message is not null)
                    {
                        this._messages.Enqueue(message);
                        this.MessageReceived?.Invoke(this, new MessageReceivedEventArgs { Message = message });
                    }
                };

                // allows thread to continue accepting messages once one comes in
                this.MessageReceived += (_, _) => this._communicationHandle.Release();

                IMessage msg = await this.WaitForMessage(this._cancellationToken);

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

                while (!this._cancellationToken.IsCancellationRequested && !this._isDisconnected)
                {
                    // the server never sends its own messages except as a consequence of a client's message
                    while (this._messages.TryDequeue(out IMessage? message))
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

                // separate variable for the event bc disconnection is only important for sending messages
                if (!Interlocked.CompareExchange(ref this._hasRunDisconnect, true, false))
                {
                    this.Disconnected?.Invoke(this, new DisconnectedEventArgs());
                }

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
                this._isDisconnected = true;

                if (!Interlocked.CompareExchange(ref this._hasRunDisconnect, true, false))
                {
                    this.Disconnected?.Invoke(this, new DisconnectedEventArgs());
                }

                this.Dispose();
            }
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

        private async Task HandleMessage(IMessage message, CancellationToken ct = default)
        {
            switch (message)
            {
                case FileRequestMessage request:
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

                    break;

                case ListFilesMessage list:
                    await this.CheckPath(list.Path, ct);
                    await this.SendListing(list.Path, ct);
                    break;

                case DeleteMessage delete:
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
                    break;

                case MakeDirMessage makeDir:
                    await this.CheckPath(makeDir.Path, ct);

                    FileTransferUtils.CreateDirectory(makeDir.Path);
                    await this.SendMessage(new ControlAckMessage { ControlCode = (byte)MessageType.MakeDir }, ct);
                    break;

                case DataMessage data:
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

                    break;

                case DataFinishedMessage finished:
                    await this.CheckPath(finished.Path, ct);
                    FileTransferUtils.FinalizeDownload(finished.Path, this._activeDownloads);

                    this.TransferFinished?.Invoke(this,
                        new TransferFinishedEventArgs { Path = finished.Path, IsSending = false });

                    break;

                case EndMessage:
                    await this.SendMessage(new EndAckMessage(), ct);
                    this._isDisconnected = true;
                    break;

                case StatusRequestMessage status:
                    await this.SendMessage(new StatusResponseMessage { Random = status.Random }, ct);
                    break;

                case ResendRequestMessage resend:
                    await this.CheckPath(resend.Path, ct);
                    await FileTransferUtils.ResendBlockAsync(resend, this.SendMessage, ct);
                    break;

                default:
                    throw new CommunicationsException($"Did not expect message of type " +
                                                      $"{message.GetType()} at this point");
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
            MemoryStream stream = new();

            // skip the size, write out the message, then go back to write the real size
            // (means no extra copy)
            stream.Seek(sizeof(uint), SeekOrigin.Begin);
            FileTransferUtils.SendMessage(stream, message);
            uint size = (uint)stream.Length - sizeof(uint);

            stream.Seek(0, SeekOrigin.Begin);
            await StreamIO.WriteUInt32Async(stream, size, ct);
            
            await this._connection.Channel.SendAsync(stream.ToArray(), ct);
        }

        private async Task SendListing(string path, CancellationToken ct = default)
        {
            if (!Directory.Exists(path))
            {
                await this.SendMessage(new ListFilesResponseMessage { Path = path, Files = [] }, ct);
                return;
            }

            string[] directories = Directory.GetDirectories(path);
            string[] files = Directory.GetFiles(path);
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

            await this.SendMessage(new ListFilesResponseMessage { Path = path, Files = entries }, ct);
        }
    }
}