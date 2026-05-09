using System.IO;

namespace AdLib.IO.Messages;

public struct DataFinishedMessage : IMessage
{
    public MessageType Header => MessageType.DataFinished;
    public string Path;
    public void Serialize(Stream stream) => StreamIO.WriteString(stream, this.Path);
    public void Deserialize(Stream stream) => this.Path = StreamIO.ReadString(stream);
}
