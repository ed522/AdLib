using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AdLib.IO;

public interface IFileSystemService
{
    public interface IFilesystemComponent;

    public struct ResendInformation
    {
        public required string Path { get; init; }
        public required ulong Offset { get; init; }
    }

    public struct PathInfo : IFilesystemComponent
    {
        public required string Path { get; init; }
    }

    public struct DataBlock : IFilesystemComponent
    {
        public required string Path { get; init; }
        public required ulong Offset { get; init; }
        public required byte[] Data { get; init; }
        public required uint Crc32 { get; init; }
        public bool IsLastBlock { get; init; }
    }

    /// <summary>
    ///     Processes a chunk of an in-progress download, writing it to a temporary <c>.[random].part</c>
    ///     file to avoid corrupting the file if the connection is lost. If the message is the first
    ///     occurrence of a given path, a new temporary file is created. If the download has ended, a
    ///     <c>DataFinishedMessage</c> is required to be sent so that the temporary file can be moved to
    ///     the final destination.
    /// </summary>
    /// <param name="path">the nominal target path of the download</param>
    /// <param name="data">the chunk of data that should be written out</param>
    /// <param name="offset">the byte offset at which the chunk starts</param>
    /// <param name="crc32">the expected CRC-32 checksum of <paramref name="data" /></param>
    /// <param name="ct">a cancellation token that can be used to cancel the processing of this chunk</param>
    Task<ResendInformation?> ProcessDownloadChunkAsync(
        string path, byte[] data, ulong offset, uint crc32, CancellationToken ct = default
    );

    /// <summary>
    ///     Uploads a local file or directory tree to the remote host.
    /// </summary>
    /// <param name="localPath">the local path to read from</param>
    /// <param name="remotePath">the remote path to write to</param>
    /// <param name="ct">a cancellation token that can be used to cancel the upload</param>
    /// <param name="fileDataAction">the action to take on each block of file data</param>
    /// <param name="directoryCreateAction">the action to take on each directory that needs to be created</param>
    Task UploadPathAsync(
        string localPath, string remotePath,
        ChannelWriter<DataBlock> fileDataAction,
        ChannelWriter<PathInfo> directoryCreateAction,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Resends a block of an already-uploaded file starting at the specified offset.
    /// </summary>
    /// <param name="path">the path of the file to read from</param>
    /// <param name="offset">the byte offset at which the block starts</param>
    /// <param name="ct">a cancellation token that can be used to cancel the resend</param>
    Task<DataBlock> ResendBlockAsync(string path, ulong offset, CancellationToken ct = default);

    /// <summary>
    ///     Creates the specified directory, along with any missing parent directories.
    /// </summary>
    /// <param name="path">the directory path to create</param>
    void CreateDirectory(string path);

    /// <summary>
    ///     Deletes the file or directory (recursively) at the specified path, if it exists.
    /// </summary>
    /// <param name="path">the path to delete</param>
    void Delete(string path);

    /// <summary>
    ///     Finalizes an in-progress download by moving its temporary <c>.part</c> file to the final
    ///     destination.
    /// </summary>
    /// <param name="path">the nominal target path of the download</param>
    void FinalizeDownload(string path);
}