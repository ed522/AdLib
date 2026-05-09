using System.IO;

namespace AdLib.IO.Messages;

public struct ControlAckMessage : IMessage
{
    public MessageType Header => MessageType.ControlAck;
    public byte ControlCode;

    public void Serialize(Stream s) => s.WriteByte(this.ControlCode);

    public void Deserialize(Stream s)
    {
        int result = s.ReadByte();
        if (result == -1) throw new EndOfStreamException();
        this.ControlCode = (byte)result;
    }
}
