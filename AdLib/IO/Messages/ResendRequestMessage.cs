using System;

namespace AdLib.IO.Messages;

public class ResendRequestMessage : IMessage
{
    public MessageType Header => MessageType.ResendRequest;
    public string Path;
    public ulong Offset;

    public Span<byte> Serialize()
    {
        Span<byte> buffer = new byte[BufferIO.GetStringLength(this.Path) + sizeof(ulong)];
        BufferIO.WriteUInt64(buffer, this.Offset, out int index);
        BufferIO.WriteString(buffer[index..], this.Path, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.Offset = BufferIO.ReadUInt64(buffer, out int index);
        this.Path = BufferIO.ReadString(buffer[index..], out _);
    }
}
