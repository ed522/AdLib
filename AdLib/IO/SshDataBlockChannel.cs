using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.DevTunnels.Ssh;

using Buffer = Microsoft.DevTunnels.Ssh.Buffer;

namespace AdLib.IO;

public sealed class SshDataBlockChannel : IDataBlockChannel
{
    public event EventHandler<EventArgs>? MessageReceived;

    private const int MaxBlockSize = 32768;
    private const long SmallBufferSizeLimit = 8192;

    private readonly SshChannel _channel;

    private readonly Channel<byte[]> _incomingMessages;

    private readonly SemaphoreSlim _messageSendSemaphore = new(128, 128);
    private readonly List<byte> _activeTransactionsOutgoing = [];
    private readonly Dictionary<byte, MemoryStream> _activeTransactionsIncoming = [];
    private readonly Lock _transactionLockOutgoing = new();
    private readonly Stack<MemoryStream> _unusedStreams = [];

    public SshDataBlockChannel(SshChannel channel)
    {
        this._channel = channel;
        this._incomingMessages = Channel.CreateUnbounded<byte[]>();

        this._channel.DataReceived += (_, weirdBuffer) =>
        {
            var buffer = weirdBuffer.Span;
            if (buffer.Length == 0) return;

            byte[] block;
            byte header = buffer[0];
            byte transactionId = (byte)(header & 0x7F);

            // continue - buffer and exit early
            if ((header & 0x80) != 0)
            {
                if (this._activeTransactionsIncoming.TryGetValue(transactionId, out var stream1))
                {
                    stream1.Write(buffer[1..]);
                }
                else
                {
                    MemoryStream newStream;

                    if (this._unusedStreams.Count > 0)
                    {
                        newStream = this._unusedStreams.Pop();
                        newStream.Seek(0, SeekOrigin.Begin);
                        newStream.SetLength(0);
                    }
                    else
                    {
                        newStream = new MemoryStream();
                    }

                    this._activeTransactionsIncoming.Add(transactionId, newStream);
                    newStream.Write(buffer[1..]);
                }

                return;
            }

            // terminal - if there's buffer data, use that; otherwise use the block as-is
            if (this._activeTransactionsIncoming.TryGetValue(transactionId, out var stream2))
            {
                stream2.Write(buffer[1..]);
                block = stream2.ToArray();
                this._activeTransactionsIncoming.Remove(transactionId);

                if (stream2.Length < SmallBufferSizeLimit)
                {
                    stream2.SetLength(0);
                    this._unusedStreams.Push(stream2);
                }
            }
            else
            {
                // get rid of header
                block = [.. buffer[1..]];
            }

            this._incomingMessages.Writer.TryWrite(block);

            this.MessageReceived?.Invoke(this, EventArgs.Empty);
        };
    }

    public byte[] GetBlock()
    {
        byte[]? result = null;
        while (result is null) result = this.GetBlock(-1);
        return result;
    }

    public byte[]? GetBlock(int timeoutMs)
    {
        CancellationTokenSource cts;

        if (timeoutMs >= 0)
        {
            cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        }
        else
        {
            cts = new CancellationTokenSource();
        }

        byte[]? item;
        while (!this._incomingMessages.Reader.TryRead(out item) && !cts.IsCancellationRequested) ;
        return item;
    }

    public byte[]? GetBlockImmediately()
    {
        this._incomingMessages.Reader.TryRead(out byte[]? item);
        return item;
    }

    public bool HasBlock() => this._incomingMessages.Reader.TryPeek(out _);

    public bool TryGetBlockImmediately([NotNullWhen(true)] out byte[]? block)
    {
        block = this.GetBlockImmediately();
        return block != null;
    }

    public void PutBlock(byte[] data)
    {
        if (data.Length != 0) this.PutBlockAsyncInternal(data, CancellationToken.None).Wait();
    }

    public async Task<byte[]> GetBlockAsync(CancellationToken token = default) => await this._incomingMessages.Reader.ReadAsync(token);

    public Task PutBlockAsync(byte[] data, CancellationToken token = default)
    {
        if (data.Length != 0) return this.PutBlockAsyncInternal(data, token);
        return Task.CompletedTask;
    }

    private async Task PutBlockAsyncInternal(byte[] data, CancellationToken token)
    {
        await this._messageSendSemaphore.WaitAsync(token);

        byte? transactionId = null;

        try
        {
            lock (this._transactionLockOutgoing)
            {
                for (byte i = 0; i < 0x7F; i++)
                {
                    if (!this._activeTransactionsOutgoing.Contains(i))
                    {
                        this._activeTransactionsOutgoing.Add(i);
                        transactionId = i;
                        break;
                    }
                }
            }

            if (transactionId == null)
            {
                throw new InvalidOperationException(
                    "Transaction still recorded, even though the semaphore was released?");
            }

            byte[] buffer = new byte[MaxBlockSize];

            for (int i = 0; i < data.Length; i += MaxBlockSize - 1)
            {
                if (i + MaxBlockSize - 1 < data.Length)
                {
                    // continue
                    buffer[0] = (byte)(transactionId.Value | 0x80);
                    Array.Copy(data, i, buffer, 1, MaxBlockSize - 1);
                }
                else
                {
                    // terminal
                    buffer = new byte[data.Length - i + 1];
                    buffer[0] = transactionId.Value;
                    Array.Copy(data, i, buffer, 1, data.Length - i);
                }

                await this._channel.SendAsync(Buffer.From(buffer), token);
            }
        }
        finally
        {
            lock (this._transactionLockOutgoing)
            {
                if (transactionId != null)
                {
                    this._activeTransactionsOutgoing.Remove(transactionId.Value);
                }
            }

            this._messageSendSemaphore.Release();
        }
    }

    public void Dispose()
    {
        this._channel.Dispose();
        this._messageSendSemaphore.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await this._channel.CloseAsync();
        this._channel.Dispose();
        this._messageSendSemaphore.Dispose();
    }
}