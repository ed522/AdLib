using System.IO;

namespace AdLib.IO.Messages;

public struct FileRequestMessage : IMessage
{
    public MessageType Header => MessageType.FileRequest;
    public string Path;
    public void Serialize(Stream stream) => BitUtils.WriteString(stream, this.Path);
    public void Deserialize(Stream stream) => this.Path = BitUtils.ReadString(stream);
}
