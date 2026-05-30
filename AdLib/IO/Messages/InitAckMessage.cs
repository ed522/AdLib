using System.IO;

namespace AdLib.IO.Messages;

public struct InitAckMessage : IMessage
{
    public MessageType Header => MessageType.InitAck;
    public string SharedFolderPath;
    public void Serialize(Stream stream) => StreamIO.WriteString(stream, this.SharedFolderPath);
    public void Deserialize(Stream stream) => this.SharedFolderPath = StreamIO.ReadString(stream);
}
