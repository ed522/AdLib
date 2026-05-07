namespace AdLib.IO.Message;

public struct HashCheckMessage : IMessage
{
    public byte Header => IMessage.FRAME_HASH_CHECK;
    public string Path;
    public byte[] ExpectedHash; // 32 bytes

    public void Serialize(System.IO.Stream s)
    {
        BitUtils.WriteString(s, this.Path);
        BitUtils.WriteFixed(s, this.ExpectedHash ?? new byte[32]);
    }

    public void Deserialize(System.IO.Stream s)
    {
        this.Path = BitUtils.ReadString(s);
        this.ExpectedHash = BitUtils.ReadFixed(s, 32);
    }
}
