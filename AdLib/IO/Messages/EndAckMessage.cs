namespace AdLib.IO.Messages;

public struct EndAckMessage : IMessage
{
    public MessageType Header => MessageType.EndAck;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
