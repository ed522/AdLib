using System.IO;

namespace AdLib.IO.Messages;

public struct EndAckMessage : IMessage
{
    public MessageType Header => MessageType.EndAck;
    public void Serialize(Stream stream) { }
    public void Deserialize(Stream stream) { }
}
