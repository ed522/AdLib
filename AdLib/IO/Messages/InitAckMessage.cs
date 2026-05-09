using System.IO;

namespace AdLib.IO.Messages;

public struct InitAckMessage : IMessage
{
    public MessageType Header => MessageType.InitAck;
    public void Serialize(Stream stream) { }
    public void Deserialize(Stream stream) { }
}
