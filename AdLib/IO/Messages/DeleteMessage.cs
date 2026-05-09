using System.IO;

namespace AdLib.IO.Messages;

public struct DeleteMessage : IMessage
{
    public MessageType Header => MessageType.Delete;
    public string Path;

    public void Serialize(Stream s) => StreamIO.WriteString(s, this.Path);
    public void Deserialize(Stream s) => this.Path = StreamIO.ReadString(s);
}
