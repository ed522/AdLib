using System;

namespace AdLib.IO.Messages;

public class ControlAckMessage : IMessage
{
    public MessageType Header => MessageType.ControlAck;
    public byte ControlCode;

    public Span<byte> Serialize()
    {
        Span<byte> buffer = new byte[1];
        buffer[0] = this.ControlCode;
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 1) throw new ArgumentException("Not enough data");
        this.ControlCode = buffer[0];
    }
}
