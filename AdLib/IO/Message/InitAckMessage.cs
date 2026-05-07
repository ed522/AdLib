namespace AdLib.IO.Message;

public struct InitAckMessage : IMessage
{
    public byte Header => IMessage.FRAME_INIT_ACK;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
