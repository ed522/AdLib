using System;

namespace AdLib.IO.Messages;

public class EndMessage : IMessage
{
    public MessageType Header => MessageType.End;
    public Span<byte> Serialize() => Span<byte>.Empty;
    public void Deserialize(ReadOnlySpan<byte> _) { }
}
