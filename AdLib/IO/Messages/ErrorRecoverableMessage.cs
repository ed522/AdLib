using System;

namespace AdLib.IO.Messages;

public class ErrorRecoverableMessage : IMessage
{
    public MessageType Header => MessageType.ErrorRecoverable;
    public RecoverableError Errno;

    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[sizeof(uint)];
        BufferIO.WriteUInt32(buffer, (uint)this.Errno, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> s) => this.Errno = (RecoverableError)BufferIO.ReadUInt32(s, out _);
}
