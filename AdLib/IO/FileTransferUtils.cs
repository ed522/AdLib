using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

using AdLib.IO.Messages;

namespace AdLib.IO;

public static class FileTransferUtils
{
    private const int FILE_BUFFER_SIZE = 32768;

    /// <summary>
    ///     Reads a message from the given stream. One byte is read from the stream to determine the
    ///     message type,
    ///     then the rest is deserialized using the appropriate struct.
    /// </summary>
    /// <param name="stream">the stream to read from</param>
    /// <returns>the deserialized message (an appropriate implementing struct of <c>IMessage</c>)</returns>
    /// <exception cref="EndOfStreamException">if the end of the stream is reached while reading</exception>
    /// <exception cref="InvalidDataException">if an unknown message ID is encountered</exception>
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
            _ => throw new InvalidDataException($"Unknown message header byte: 0x{headerByte:X2}"),
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
    public static void ProcessDownloadChunk(
        DataMessage data,
        Dictionary<string, (string partPath, FileStream stream)> activeDownloads,
        Random random
    )
    {
        if (!activeDownloads.TryGetValue(data.Path, out (string partPath, FileStream stream) download))
        {
            const int PART_RANDOM_EXTENSION_LENGTH = 8 * 6 / 8; // first number controls character count (8)
            string partPath;

            // there's an incredibly small chance of a collision but we should still handle it
            // TECHNICALLY you can create 281 trillion files using every single extension but that's your
            // fault if you do and this breaks
            do
            {
                byte[] randomBytes = new byte[PART_RANDOM_EXTENSION_LENGTH];
                random.NextBytes(randomBytes);
                partPath = $"{data.Path}.{Convert.ToBase64String(randomBytes)}.part";
            } while (File.Exists(partPath));

            string? directory = Path.GetDirectoryName(data.Path);
            if (directory != null) Directory.CreateDirectory(directory);

            FileStream stream = File.Create(partPath);
            download = (partPath, stream);
            activeDownloads[data.Path] = download;
        }

        if (data.Crc32 != Crc32.HashToUInt32(data.Data))
        {
            // TODO do something?
        }
        download.stream.Write(data.Data);
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

    public static void UploadPath(
        string localPath,
        string remotePath,
        Action<IMessage> sendMessageAction
    )
    {
        if (File.Exists(localPath))
        {
            try
            {
                // upload file in chunks to avoid massive buffering requirements
                byte[] data = new byte[FILE_BUFFER_SIZE];
                ulong totalDataSize = (ulong)new FileInfo(localPath).Length; // cannot be negative
                ulong currentOffset = 0;
                using FileStream fileStream = File.OpenRead(localPath);

                while (currentOffset < totalDataSize)
                {
                    int read = fileStream.Read(data);
                    if (read == 0) break; // finished
                    currentOffset += (uint)read; // read also shouldn't be negative

                    byte[] slice = read < data.Length ? data[..read] : data;

                    sendMessageAction(new DataMessage
                    {
                        Path = remotePath,
                        Data = slice,
                        Crc32 = Crc32.HashToUInt32(slice),
                    });
                }

                sendMessageAction(new DataFinishedMessage { Path = remotePath });
            }
            catch (Exception ex) when (ex is IOException)
            {
                sendMessageAction(new ErrorRecoverableMessage());
            }
        }
        else if (Directory.Exists(localPath))
        {
            sendMessageAction(new MakeDirMessage { Path = remotePath });

            foreach (string file in Directory.GetFiles(localPath))
            {
                // plain upload (does not recurse further than this call)
                UploadPath(file, Path.Combine(remotePath, Path.GetFileName(file)), sendMessageAction);
            }

            foreach (string dir in Directory.GetDirectories(localPath))
            {
                // recurses
                UploadPath(dir, Path.Combine(remotePath, Path.GetFileName(dir)), sendMessageAction);
            }
        }
        else
        {
            throw new FileNotFoundException("Could not find file or directory", localPath);
        }
    }
}
