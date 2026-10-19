using System;

namespace AdLib.IO.Messages;

public class StatusRequestMessage : IMessage
{
    public MessageType Header => MessageType.StatusRequest;
    public uint Random;

    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[sizeof(uint)];
        BufferIO.WriteUInt32(buffer, this.Random, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> s) => this.Random = BufferIO.ReadUInt32(s, out _);
}
