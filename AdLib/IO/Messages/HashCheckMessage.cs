using System.IO;

namespace AdLib.IO.Messages;

public struct HashCheckMessage : IMessage
{
    public MessageType Header => MessageType.HashCheck;
    public string Path;
    public byte[] ExpectedHash; // 32 bytes

    public void Serialize(Stream s)
    {
        StreamIO.WriteString(s, this.Path);
        StreamIO.WriteFixed(s, this.ExpectedHash ?? new byte[32]);
    }

    public void Deserialize(Stream s)
    {
        this.Path = StreamIO.ReadString(s);
        this.ExpectedHash = StreamIO.ReadFixed(s, 32);
    }
}
