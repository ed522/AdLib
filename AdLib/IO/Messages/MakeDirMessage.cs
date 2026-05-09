using System.IO;

namespace AdLib.IO.Messages;

public struct MakeDirMessage : IMessage
{
    public MessageType Header => MessageType.MakeDir;
    public string Path;

    public void Serialize(Stream s) => StreamIO.WriteString(s, this.Path);
    public void Deserialize(Stream s) => this.Path = StreamIO.ReadString(s);
}
