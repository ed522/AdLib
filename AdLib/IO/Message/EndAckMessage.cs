namespace AdLib.IO.Message;

public struct EndAckMessage : IMessage
{
    public byte Header => IMessage.FRAME_END_ACK;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
