using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AdLib.IO.Files;
using AdLib.IO.Messages;

namespace AdLib.IO;

public static class FileTransferUtils
{
    private const int FileBufferSize = 32768;

    /// <summary>
    ///     Reads a message from the given stream. One byte is read from the stream to determine the
    ///     message type,
    ///     then the rest is deserialized using the appropriate struct.
    /// </summary>
    /// <param name="stream">the stream to read from</param>
    /// <returns>the deserialized message (an appropriate implementing struct of <c>IMessage</c>)</returns>
    /// <exception cref="EndOfStreamException">if the end of the stream is reached while reading</exception>
    /// <exception cref="CommunicationsException">if an unknown message ID is encountered</exception>
    public static IMessage ReadMessage(Stream stream)
    {
        // determines type
        int headerByte = stream.ReadByte();

        if (headerByte == -1)
        {
            throw new EndOfStreamException("Connection unexpectedly closed by remote host");
        }

        MessageType type = (MessageType)headerByte;

        // NOTE: make sure that the switch is exhaustive
        IMessage message = type switch
        {
            MessageType.Init => new InitMessage(),
            MessageType.InitAck => new InitAckMessage(),

            MessageType.Data => new DataMessage(),
            MessageType.DataFinished => new DataFinishedMessage(),
            MessageType.FileRequest => new FileRequestMessage(),
            MessageType.ResendRequest => new ResendRequestMessage(),

            MessageType.StatusRequest => new StatusRequestMessage(),
            MessageType.StatusResponse => new StatusResponseMessage(),
            MessageType.MakeDir => new MakeDirMessage(),
            MessageType.ListFiles => new ListFilesMessage(),
            MessageType.ListFilesResponse => new ListFilesResponseMessage(),
            MessageType.Delete => new DeleteMessage(),
            MessageType.HashCheck => new HashCheckMessage(),
            MessageType.HashResponse => new HashResponseMessage(),
            MessageType.ControlAck => new ControlAckMessage(),

            MessageType.End => new EndMessage(),
            MessageType.EndAck => new EndAckMessage(),
            MessageType.ErrorRecoverable => new ErrorRecoverableMessage(),
            MessageType.ErrorFatal => new ErrorFatalMessage(),
            _ => throw new CommunicationsException($"Unknown message header byte: 0x{headerByte:X2}"),
        };

        // this initializes all of the fields on the struct
        message.Deserialize(stream);
        return message;
    }

    public static void SendMessage(Stream stream, IMessage message)
    {
        stream.WriteByte((byte)message.Header);
        message.Serialize(stream);
        stream.Flush();
    }

    public static uint BufferMessage(
        uint expectedSize, byte[] buffer, MemoryStream inProgressBuffer, out IMessage? possibleMessage
    )
    {
        using MemoryStream stream = new(buffer);

        // _currentExpectedSize == 0 when no buffering operation is in progress
        if (expectedSize == 0)
        {
            expectedSize = StreamIO.ReadUInt32(stream);

            // is the entire message in this packet?
            if (stream.Length == expectedSize + sizeof(uint))
            {
                // read it all at once - a copy is unnecessary
                possibleMessage = ReadMessage(stream);
                return 0;
            }
            // else: there's not enough data here - start a buffered operation
        }

        // only reachable on buffered operation
        stream.CopyTo(inProgressBuffer);

        // is there a buffered message that has been completely satisfied?
        if (expectedSize != 0 && inProgressBuffer.Length >= expectedSize)
        {
            // deserialize and reset
            inProgressBuffer.Position = 0;
            possibleMessage = ReadMessage(inProgressBuffer);
            inProgressBuffer.SetLength(0);
            return 0;
        }

        // else: continue buffering
        possibleMessage = null;
        return expectedSize;
    }

    /// <summary>
    ///     Processes a chunk of an in-progress download, writing it to a temporary <c>.[random].part</c>
    ///     file to avoid corrupting the file if the connection is lost. If the message is the first
    ///     occurrence of a given path, a new temporary file is created. If the download has ended, a
    ///     <c>DataFinishedMessage</c> is required to be sent so that the temporary file can be moved to
    ///     the final destination.
    /// </summary>
    /// <param name="data">the chunk of data that should be written out</param>
    /// <param name="activeDownloads">
    ///     a set of in-progress downloads to use. The key is the nominal target path that was sent by the
    ///     remote host, and the value is a tuple of the true path for the <c>.part</c> file and a stream
    ///     for
    ///     the same.
    /// </param>
    /// <param name="random">
    ///     a non-cryptographic <c>Random</c> instance that is used for file naming to lessen
    ///     the likelihood of collisions.
    /// </param>
    /// <param name="sendMessageAction">an action that sends a message to the remote host</param>
    /// <param name="ct">a cancellation token that can be used to cancel the processing of this chunk</param>
    public static async Task ProcessDownloadChunkAsync(
        DataMessage data,
        Dictionary<string, (string partPath, FileStream stream)> activeDownloads,
        Random random,
        Func<IMessage, CancellationToken, Task> sendMessageAction,
        CancellationToken ct = default
    )
    {
        if (!activeDownloads.TryGetValue(data.Path, out (string partPath, FileStream stream) download))
        {
            const int partRandomExtensionLength = 8 * 6 / 8; // first number controls character count (8)
            string partPath;

            // check for collisions
            // there's a case-insensitive collision if: 
            // 1. the filename already exists, and
            // 2. there is no case-sensitive match

            // this approach detects collisions on case-insensitive filesystems but not on case-sensitive
            // ones, nor for edge cases on case-sensitive parts of otherwise insensitive filesystems (without
            // having to write to a temp file)

            // this also ignores collisions on case-insensitive filesystems in the case that the files have
            // case-sensitively identical filenames, in which case we replace the contents

            if (File.Exists(data.Path) &&
                !Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(data.Path)) ?? "")
                          .Any(f => f == data.Path))
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.CaseConflict);
            }

            if (data.Path.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.InvalidCharacters);
            }

            // there's an incredibly small chance of a collision but we should still handle it
            // TECHNICALLY you can create 281 trillion files using every single extension but that's your
            // fault if you do and this breaks
            do
            {
                byte[] randomBytes = new byte[partRandomExtensionLength];
                random.NextBytes(randomBytes);
                partPath = $"{data.Path}.{Convert.ToBase64String(randomBytes)}.part";
            } while (File.Exists(partPath));

            // create the directory if it doesn't exist
            string? directory = Path.GetDirectoryName(data.Path);
            if (directory != null) CreateDirectory(directory);

            // open a stream and store it for future use (overwrite if necessary)
            FileStream stream = File.OpenWrite(partPath);
            download = (partPath, stream);
            activeDownloads[data.Path] = download;
        }

        // arrives after file finishes
        if (data.Crc32 != Crc32.HashToUInt32(data.Data))
        {
            ResendRequestMessage resendRequest = new()
            {
                Path = data.Path,
                Offset = (ulong)download.stream.Position,
            };

            await sendMessageAction(resendRequest, ct);
        }

        await download.stream.WriteAsync(data.Data, ct);
    }

    public static Task ProcessDownloadChunkAsync(
        DataMessage data,
        Dictionary<string, (string partPath, FileStream stream)> activeDownloads,
        Random random,
        Func<IMessage, Task> sendMessageAction,
        CancellationToken ct = default
    )
    {
        return ProcessDownloadChunkAsync(data, activeDownloads, random, (msg, _) => sendMessageAction(msg), ct);
    }
    
    public static void CreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            // @see ProcessDownloadChunk for explanation

            if (Directory.Exists(path) &&
                !Directory.GetDirectories(Path.GetFullPath(path))
                          .Any(f => f == path))
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.CaseConflict);
            }

            return;
        }

        Directory.CreateDirectory(path);
    }

    public static void FinalizeDownload(
        string path,
        Dictionary<string, (string partPath, FileStream stream)> activeDownloads
    )
    {
        if (activeDownloads.Remove(path, out (string partPath, FileStream stream) download))
        {
            download.stream.Dispose();
            if (File.Exists(path)) File.Delete(path);
            File.Move(download.partPath, path);
        }
    }

    public static async Task UploadPathAsync(
        string localPath,
        string remotePath,
        Func<IMessage, CancellationToken, Task> sendMessageAction,
        CancellationToken ct = default
    )
    {
        if (File.Exists(localPath))
        {
            try
            {
                // upload file in chunks to avoid massive buffering requirements
                byte[] data = new byte[FileBufferSize];
                ulong totalDataSize = (ulong)new FileInfo(localPath).Length; // cannot be negative
                ulong currentOffset = 0;
                await using FileStream fileStream = File.OpenRead(localPath);

                while (currentOffset < totalDataSize)
                {
                    int read = await fileStream.ReadAsync(data, ct);
                    if (read == 0) break; // finished
                    currentOffset += (uint)read; // read also shouldn't be negative

                    byte[] slice = read < data.Length ? data[..read] : data;

                    await sendMessageAction(new DataMessage
                    {
                        Path = remotePath,
                        Data = slice,
                        Crc32 = Crc32.HashToUInt32(slice),
                    }, ct);
                }

                await sendMessageAction(new DataFinishedMessage { Path = remotePath }, ct);
            }
            catch (Exception ex) when (ex is IOException)
            {
                await sendMessageAction(new ErrorRecoverableMessage { Errno = RecoverableError.ERRNO_IO_ERROR }, ct);
            }
        }
        else if (Directory.Exists(localPath))
        {
            // perform a depth-first search of the directory

            // make the directory (doesn't error if already exists, but will error if there's a case
            // conflict)
            await sendMessageAction(new MakeDirMessage { Path = remotePath }, ct);

            List<Task> inProgressUploads = [];

            // file includes full path + filename
            await foreach (string file in AsyncFiles.EnumerateFilesAsync(localPath, cancellationToken: ct))
            {
                // directly upload file (no further recursion after this)
                inProgressUploads.Add(
                    UploadPathAsync(file, Path.Combine(remotePath, Path.GetFileName(file)), sendMessageAction, ct)
                );
            }

            await Task.WhenAll(inProgressUploads);
            inProgressUploads.Clear();

            // dir includes full path + dir name
            await foreach (string dir in AsyncFiles.EnumerateDirectoriesAsync(localPath, cancellationToken: ct))
            {
                // recursively upload all directories - based on local & remote subdirectories of the current base
                inProgressUploads.Add(
                    UploadPathAsync(dir, Path.Combine(remotePath, Path.GetFileName(dir)), sendMessageAction, ct)
                );
            }

            await Task.WhenAll(inProgressUploads);
        }
        else
        {
            throw new FileNotFoundException("Could not find file or directory", localPath);
        }
    }

    public static Task ResendBlockAsync(
        ResendRequestMessage resend, Func<IMessage, Task> sendMessageAction, CancellationToken ct = default
    )
    {
        return ResendBlockAsync(resend, (msg, _) => sendMessageAction(msg), ct);
    }

    public static async Task ResendBlockAsync(
        ResendRequestMessage resend,
        Func<IMessage, CancellationToken, Task> sendMessageAction,
        CancellationToken ct = default
    )
    {
        await using FileStream tmpStream = File.Open(resend.Path, FileMode.Open, FileAccess.Read,
            FileShare.Read);

        tmpStream.Seek((long)resend.Offset, SeekOrigin.Begin);
        byte[] buffer = new byte[FileBufferSize];
        int read = tmpStream.Read(buffer);

        DataMessage data = new()
        {
            Path = resend.Path,
            Data = buffer[..read],
        };

        await sendMessageAction(data, ct);
    }
}