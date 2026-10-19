using System;

namespace AdLib.IO.Messages;

public class InitMessage : IMessage
{
    public MessageType Header => MessageType.Init;
    public char PathSeparator;

    public Span<byte> Serialize() => new[] { (byte)this.PathSeparator };
    public void Deserialize(ReadOnlySpan<byte> buffer) => this.PathSeparator = (char)buffer[0];
}
