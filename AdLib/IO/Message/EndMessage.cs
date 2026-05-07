namespace AdLib.IO.Message;

public struct EndMessage : IMessage
{
    public byte Header => IMessage.FRAME_END;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
