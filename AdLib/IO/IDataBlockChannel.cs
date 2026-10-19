using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace AdLib.IO;

public interface IDataBlockChannel : IDisposable, IAsyncDisposable
{
    public event EventHandler<EventArgs> MessageReceived;

    /**
     * Gets a single block of data, blocking if there is none.
     */
    public byte[] GetBlock();

    /**
     * Gets a single block of data, returning null if there is none immediately available.
     */
    public byte[]? GetBlockImmediately();

    /**
     * Gets a single block of data, blocking for up to timeoutMs milliseconds, returning null if there is no available block.
     * <param name="timeoutMs">The timeout to wait for in milliseconds, or -1 to wait forever.</param>
     */
    public byte[]? GetBlock(int timeoutMs);

    public bool TryGetBlockImmediately([NotNullWhen(true)] out byte[]? block);
    public bool HasBlock();

    /**
     * Synchronously sends a single block of data.
     */
    public void PutBlock(byte[] data);

    /**
     * Gets a single block of data from the stream asynchronously.
     */
    public Task<byte[]> GetBlockAsync(CancellationToken ct = default);

    /**
     * Sends a single block of data to the stream asynchronously.
     */
    public Task PutBlockAsync(byte[] data, CancellationToken ct = default);
}