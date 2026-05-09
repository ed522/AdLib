using System.IO;

namespace AdLib.IO.Messages;

public struct FileRequestMessage : IMessage
{
    public MessageType Header => MessageType.FileRequest;
    public string Path;
    public void Serialize(Stream stream) => StreamIO.WriteString(stream, this.Path);
    public void Deserialize(Stream stream) => this.Path = StreamIO.ReadString(stream);
}
