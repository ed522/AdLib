namespace AdLib.IO.Messages;

public struct InitAckMessage : IMessage
{
    public MessageType Header => MessageType.InitAck;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
