namespace AdLib.IO.Message;

public struct MakeDirMessage : IMessage
{
    public byte Header => IMessage.FRAME_MAKE_DIR;
    public string Path;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteString(s, this.Path);
    public void Deserialize(System.IO.Stream s) => this.Path = BitUtils.ReadString(s);
}
