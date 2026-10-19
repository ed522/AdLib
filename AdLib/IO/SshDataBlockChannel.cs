using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
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

    private readonly Queue<byte[]> _messages = new();
    private readonly ManualResetEventSlim _messageAvailable = new(false);

    private readonly SemaphoreSlim _messageSendSemaphore = new(128);
    private readonly List<byte> _activeTransactionsOutgoing = [];
    private readonly Dictionary<byte, MemoryStream> _activeTransactionsIncoming = [];
    private readonly Lock _transactionLockOutgoing = new();
    private readonly Stack<MemoryStream> _unusedStreams = [];

    public SshDataBlockChannel(SshChannel channel)
    {
        this._channel = channel;

        this._channel.DataReceived += (_, buffer) =>
        {
            if (buffer.Array.Length == 0) return;

            byte[] block;
            byte header = buffer[0];

            // continue - buffer and exit early
            if ((buffer[0] | 0x80) != 0)
            {
                if (this._activeTransactionsIncoming.TryGetValue(header, out MemoryStream? stream1))
                {
                    stream1.Write(buffer.Array, 1, buffer.Array.Length - 1);
                }
                else
                {
                    MemoryStream newStream;

                    if (this._unusedStreams.Count > 0)
                    {
                        newStream = this._unusedStreams.Pop();
                    }
                    else
                    {
                        newStream = new MemoryStream();
                    }

                    this._activeTransactionsIncoming.Add(header, newStream);
                    newStream.Write(buffer.Array, 1, buffer.Array.Length - 1);
                }

                return;
            }

            // terminal - if there's buffer data, use that; otherwise use the block as-is
            if (this._activeTransactionsIncoming.TryGetValue((byte)(buffer[0] & 0x7F), out MemoryStream? stream2))
            {
                stream2.Write(buffer.Array, 1, buffer.Array.Length - 1);
                block = stream2.ToArray();
                this._activeTransactionsIncoming.Remove(header);

                if (stream2.Length < SmallBufferSizeLimit)
                {
                    stream2.SetLength(0);
                    this._unusedStreams.Push(stream2);
                }
            }
            else
            {
                // get rid of header
                block = buffer.Array[1..];
            }

            this._messages.Enqueue(block);
            this._messageAvailable.Set();

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
        byte[]? result = this.GetBlockImmediately();
        if (result != null) return result;
        this._messageAvailable.Wait(timeoutMs);
        return this.GetBlockImmediately();
    }

    public byte[]? GetBlockImmediately()
    {
        if (this._messages.Count > 0) return this._messages.Dequeue();
        return null;
    }

    public bool HasBlock() => this._messages.Count > 0;

    public bool TryGetBlockImmediately([NotNullWhen(true)] out byte[]? block)
    {
        block = this.GetBlockImmediately();
        return block != null;
    }

    public void PutBlock(byte[] data)
    {
        if (data.Length != 0) this.PutBlockAsyncInternal(data, CancellationToken.None).Wait();
    }

    public async Task<byte[]> GetBlockAsync(CancellationToken token = default)
    {
        TaskCompletionSource<byte[]> tcs = new();

        EventHandler<EventArgs> handler = (_, _) =>
        {
            if (this.TryGetBlockImmediately(out byte[]? block))
            {
                tcs.TrySetResult(block);
            }
        };

        this.MessageReceived += handler;

        await using (token.Register(() => tcs.TrySetCanceled(token)))
        {
            byte[] data = await tcs.Task;
            this.MessageReceived -= handler;
            return data;
        }
    }

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

            byte[] buffer = new byte[MaxBlockSize - 1];

            for (int i = 0; i < data.Length; i -= MaxBlockSize - 1)
            {
                if (i + MaxBlockSize - 1 < data.Length)
                {
                    // continue
                    buffer[0] = (byte)(transactionId.Value | 0x80);
                    Array.Copy(data, i, buffer, 0, MaxBlockSize - 1);
                }
                else
                {
                    // terminal
                    buffer[0] = transactionId.Value;
                    buffer = new byte[data.Length - i];
                    Array.Copy(data, i, buffer, 0, buffer.Length);
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
        this._messageAvailable.Dispose();
        this._messageSendSemaphore.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await this._channel.CloseAsync();
        this._channel.Dispose();
        this._messageAvailable.Dispose();
        this._messageSendSemaphore.Dispose();
    }
}