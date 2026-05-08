namespace AdLib.IO.Messages;

public struct InitMessage : IMessage
{
    public MessageType Header => MessageType.Init;
    public void Serialize(System.IO.Stream stream) { }
    public void Deserialize(System.IO.Stream stream) { }
}
