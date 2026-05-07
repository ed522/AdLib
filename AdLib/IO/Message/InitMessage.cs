namespace AdLib.IO.Message;

public struct InitMessage : IMessage
{
    public byte Header => IMessage.FRAME_INIT;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
