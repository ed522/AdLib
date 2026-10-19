using System;

namespace AdLib.IO.Messages;

public class ErrorFatalMessage : IMessage
{
    public MessageType Header => MessageType.ErrorFatal;
    public FatalError Errno;

    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[sizeof(uint)];
        BufferIO.WriteUInt32(buffer, (uint)this.Errno, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> s) => this.Errno = (FatalError)BufferIO.ReadUInt32(s, out _);
}
