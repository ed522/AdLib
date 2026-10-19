using System;

namespace AdLib.IO.Messages;

public class EndAckMessage : IMessage
{
    public MessageType Header => MessageType.EndAck;
    public Span<byte> Serialize() => Span<byte>.Empty;
    public void Deserialize(ReadOnlySpan<byte> _) { }
}
