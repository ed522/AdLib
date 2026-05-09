using System.IO;

namespace AdLib.IO.Messages;

public struct InitMessage : IMessage
{
    public MessageType Header => MessageType.Init;
    public void Serialize(Stream stream) { }
    public void Deserialize(Stream stream) { }
}
