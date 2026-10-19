using System;

namespace AdLib.IO.Messages;

public class HashResponseMessage : IMessage
{
    public MessageType Header => MessageType.HashResponse;
    public bool Status; // 0/1, correct/update needed

    public Span<byte> Serialize() => new[] { (byte)(this.Status ? 1 : 0) };

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.Status = buffer[0] != 0;
    }
}
