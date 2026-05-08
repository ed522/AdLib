namespace AdLib.IO.Messages;

public struct EndMessage : IMessage
{
    public MessageType Header => MessageType.End;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
