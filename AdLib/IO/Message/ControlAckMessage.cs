namespace AdLib.IO.Message;

public struct ControlAckMessage : IMessage
{
    public byte Header => IMessage.FRAME_CONTROL_ACK;
    public byte ControlCode;

    public void Serialize(System.IO.Stream s) => s.WriteByte(this.ControlCode);
    public void Deserialize(System.IO.Stream s)
    {
        int result = s.ReadByte();
        if (result == -1) throw new System.IO.EndOfStreamException();
        this.ControlCode = (byte)result;
    }
}
