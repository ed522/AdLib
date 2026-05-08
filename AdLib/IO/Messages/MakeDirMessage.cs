namespace AdLib.IO.Messages;

public struct MakeDirMessage : IMessage
{
    public MessageType Header => MessageType.MakeDir;
    public string Path;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteString(s, this.Path);
    public void Deserialize(System.IO.Stream s) => this.Path = BitUtils.ReadString(s);
}
