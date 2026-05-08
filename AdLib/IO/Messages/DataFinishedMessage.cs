using System.IO;

namespace AdLib.IO.Messages;

public struct DataFinishedMessage : IMessage
{
    public MessageType Header => MessageType.DataFinished;
    public string Path;
    public void Serialize(Stream stream) => BitUtils.WriteString(stream, this.Path);
    public void Deserialize(Stream stream) => this.Path = BitUtils.ReadString(stream);
}
