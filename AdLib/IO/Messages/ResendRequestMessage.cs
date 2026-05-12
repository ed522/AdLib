using System.IO;

namespace AdLib.IO.Messages;

public struct ResendRequestMessage : IMessage
{
    public MessageType Header => MessageType.ResendRequest;
    public string Path;
    public ulong Offset;

    public void Serialize(Stream stream)
    {
        StreamIO.WriteVarInt(stream, this.Offset);
        StreamIO.WriteString(stream, this.Path);
    }

    public void Deserialize(Stream stream)
    {
        this.Offset = StreamIO.ReadVarInt(stream);
        this.Path = StreamIO.ReadString(stream);
    }
}
