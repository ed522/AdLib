using System.IO;

namespace AdLib.IO.Messages;

public struct StatusResponseMessage : IMessage
{
    public MessageType Header => MessageType.StatusResponse;
    public uint Random;

    public void Serialize(Stream s) => StreamIO.WriteUInt32(s, this.Random);
    public void Deserialize(Stream s) => this.Random = StreamIO.ReadUInt32(s);
}
