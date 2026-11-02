using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AdLib.IO.Messages;
using AdLib.Utils;

namespace AdLib.IO;

public sealed class BlockMessageChannel : IMessageChannel
{
    private readonly IDataBlockChannel _channel;
    public event EventHandler<EventArgs<IMessage>>? MessageReceived;

    public BlockMessageChannel(IDataBlockChannel channel)
    {
        this._channel = channel;

        channel.MessageReceived += (_, _) =>
            this.MessageReceived?.Invoke(this, new EventArgs<IMessage>(this.ReadMessage()));
    }

    /// <summary>
    ///     Reads a message from the given stream. One byte is read from the stream to determine the
    ///     message type,
    ///     then the rest is deserialized using the appropriate struct.
    /// </summary>
    /// <param name="data">one complete data block</param>
    /// <returns>the deserialized message (an appropriate implementing struct of <c>IMessage</c>)</returns>
    /// <exception cref="EndOfStreamException">if the end of the stream is reached while reading</exception>
    /// <exception cref="CommunicationsException">if an unknown message ID is encountered</exception>
    private static IMessage ReadMessage(ReadOnlySpan<byte> data)
    {
        // determines type
        int headerByte = data[0];

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

        // this initializes all of the fields on the class
        message.Deserialize(data[1..]);
        return message;
    }

    private static byte[] SerializeMessage(IMessage message)
    {
        Span<byte> initial = message.Serialize();
        byte[] buffer = new byte[initial.Length + 1];
        buffer[0] = (byte)message.Header;
        initial.CopyTo(buffer.AsSpan()[1..]);
        return buffer;
    }

    public IMessage? ReadMessageIfAvailable()
    {
        byte[]? data = this._channel.GetBlockImmediately();

        while (data is not null && data.Length == 0)
        {
            data = this._channel.GetBlockImmediately();
        }
        
        if (data is null) return null;
        
        return ReadMessage(data);
    }

    public IMessage? ReadMessageIfAvailable(int timeout)
    {
        byte[]? data = this._channel.GetBlock(timeout);
        if (data is null || data.Length == 0) return null;
        return ReadMessage(data);
    }

    public IMessage ReadMessage()
    {
        byte[] data = this._channel.GetBlock();
        return ReadMessage(data);
    }

    public async Task<IMessage> ReadMessageAsync(CancellationToken ct = default)
    {
        byte[] data = await this._channel.GetBlockAsync(ct);
        return ReadMessage(data);
    }

    public bool TryReadMessageIfAvailable([NotNullWhen(true)] out IMessage? message)
    {
        message = this.ReadMessageIfAvailable();
        return message != null;
    }

    public bool HasMessage() => this._channel.HasBlock();

    public void WriteMessage(IMessage message) { this._channel.PutBlock(SerializeMessage(message)); }

    public Task WriteMessageAsync(IMessage message, CancellationToken ct = default) =>
        this._channel.PutBlockAsync(SerializeMessage(message), ct);

    public void Dispose() { this._channel.Dispose(); }

    public async ValueTask DisposeAsync() { await this._channel.DisposeAsync(); }
}