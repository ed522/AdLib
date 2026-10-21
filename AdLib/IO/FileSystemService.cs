using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using static AdLib.IO.IFileSystemService;

namespace AdLib.IO;

public class FileSystemService : IFileSystemService
{
    private const int FileBufferSize = 262144;

    private readonly Random _random = new();
    private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = new();

    public async Task<ResendInformation?> ProcessDownloadChunkAsync(
        string path, byte[] data, ulong offset, uint crc32, CancellationToken ct = default
    )
    {
        if (!this._activeDownloads.TryGetValue(path, out var download))
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

            if (File.Exists(path) &&
                !Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "")
                          .Any(f => f == path))
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.CaseConflict);
            }

            if (path.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.InvalidCharacters);
            }

            // there's an incredibly small chance of a collision but we should still handle it
            // TECHNICALLY you can create 281 trillion files using every single extension but that's your
            // fault if you do and this breaks
            do
            {
                byte[] randomBytes = new byte[partRandomExtensionLength];
                this._random.NextBytes(randomBytes);
                partPath = $"{path}.{Convert.ToBase64String(randomBytes)}.part";
            } while (File.Exists(partPath));

            // create the directory if it doesn't exist
            string? directory = Path.GetDirectoryName(path);
            if (directory != null) this.CreateDirectory(directory);

            // open a stream and store it for future use (overwrite if necessary)
            var stream = File.OpenWrite(partPath);
            download = (partPath, stream);
            this._activeDownloads[path] = download;
        }

        if (crc32 != Crc32.HashToUInt32(data))
        {
            return new ResendInformation
            {
                Path = path,
                Offset = (ulong)download.stream.Position,
            };
        }

        download.stream.Seek((long)offset, SeekOrigin.Begin);
        await download.stream.WriteAsync(data, ct);
        return null;
    }

    public void CreateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            // @see ProcessDownloadChunkAsync for explanation

            if (Directory.Exists(path) && !Directory.GetDirectories(Path.GetFullPath(path)).Any(f => f == path))
            {
                throw new InvalidPathException(InvalidPathException.InvalidPathReason.CaseConflict);
            }

            return;
        }

        Directory.CreateDirectory(path);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    public void FinalizeDownload(string path)
    {
        if (this._activeDownloads.Remove(path, out var download))
        {
            download.stream.Dispose();
            if (File.Exists(path)) File.Delete(path);
            File.Move(download.partPath, path);
        }
    }

    public Task UploadPathAsync(
        string localPath, string remotePath,
        ChannelWriter<DataBlock> dataWriter, ChannelWriter<PathInfo> directoryWriter,
        CancellationToken ct = default
    ) => UploadPathAsyncInternal(localPath, remotePath, dataWriter, directoryWriter, true, ct);

    private static async Task UploadPathAsyncInternal(
        string localPath, string remotePath,
        ChannelWriter<DataBlock> dataWriter, ChannelWriter<PathInfo> directoryWriter,
        bool ownsWriters,
        CancellationToken ct = default
    )
    {
        if (File.Exists(localPath))
        {
            // upload file in chunks to avoid massive buffering requirements
            byte[] data = new byte[FileBufferSize];
            ulong totalDataSize = (ulong)new FileInfo(localPath).Length; // cannot be negative
            ulong currentOffset = 0;
            await using var fileStream = File.OpenRead(localPath);

            while (currentOffset < totalDataSize)
            {
                ulong startOffset = currentOffset;
                int read = await fileStream.ReadAsync(data, ct);
                if (read == 0) break; // finished
                currentOffset += (uint)read; // read also shouldn't be negative

                byte[] slice = read < data.Length ? data[..read] : data[..];

                await dataWriter.WriteAsync(new DataBlock
                {
                    Path = remotePath,
                    Offset = startOffset,
                    Data = slice,
                    Crc32 = Crc32.HashToUInt32(slice),
                    IsLastBlock = currentOffset >= totalDataSize,
                }, ct);
            }

            if (ownsWriters)
            {
                dataWriter.Complete();
                directoryWriter.Complete();
            }
        }
        else if (Directory.Exists(localPath))
        {
            // perform a depth-first search of the directory

            // make the directory (doesn't error if already exists, but will error if there's a case
            // conflict)
            await directoryWriter.WriteAsync(new PathInfo { Path = remotePath }, ct);

            List<Task> currentTasks = [];

            // file includes full path + filename
            foreach (string file in Directory.EnumerateFiles(localPath))
            {
                currentTasks.Add(UploadPathAsyncInternal(file, Path.Combine(remotePath, Path.GetFileName(file)),
                    dataWriter, directoryWriter, false, ct));
            }

            // dir includes full path + dir name
            foreach (string dir in Directory.EnumerateDirectories(localPath))
            {
                currentTasks.Add(UploadPathAsyncInternal(dir, Path.Combine(remotePath, Path.GetFileName(dir)),
                    dataWriter, directoryWriter, false, ct));
            }

            await Task.WhenAll(currentTasks);

            if (ownsWriters)
            {
                dataWriter.Complete();
                directoryWriter.Complete();
            }
        }
        else
        {
            throw new FileNotFoundException("Could not find file or directory", localPath);
        }
    }

    public async Task<DataBlock> ResendBlockAsync(string path, ulong offset, CancellationToken ct = default)
    {
        await using var tmpStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        tmpStream.Seek((long)offset, SeekOrigin.Begin);
        byte[] buffer = new byte[FileBufferSize];
        int read = tmpStream.Read(buffer);

        return new DataBlock
        {
            Path = path,
            Offset = offset,
            Data = buffer[..read],
            Crc32 = Crc32.HashToUInt32(buffer.AsSpan()[..read]),
            IsLastBlock = read < buffer.Length,
        };
    }
}