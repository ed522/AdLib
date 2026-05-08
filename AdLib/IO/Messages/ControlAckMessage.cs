namespace AdLib.IO.Messages;

public struct ControlAckMessage : IMessage
{
    public MessageType Header => MessageType.ControlAck;
    public byte ControlCode;

    public void Serialize(System.IO.Stream s) => s.WriteByte(this.ControlCode);
    public void Deserialize(System.IO.Stream s)
    {
        int result = s.ReadByte();
        if (result == -1) throw new System.IO.EndOfStreamException();
        this.ControlCode = (byte)result;
    }
}
