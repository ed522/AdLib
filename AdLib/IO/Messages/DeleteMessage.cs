namespace AdLib.IO.Messages;

public struct DeleteMessage : IMessage
{
    public MessageType Header => MessageType.Delete;
    public string Path;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteString(s, this.Path);
    public void Deserialize(System.IO.Stream s) => this.Path = BitUtils.ReadString(s);
}
