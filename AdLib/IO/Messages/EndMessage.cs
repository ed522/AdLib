using System.IO;

namespace AdLib.IO.Messages;

public struct EndMessage : IMessage
{
    public MessageType Header => MessageType.End;
    public void Serialize(Stream stream) { }
    public void Deserialize(Stream stream) { }
}
