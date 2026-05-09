using System.IO;

namespace AdLib.IO.Messages;

public struct StatusRequestMessage : IMessage
{
    public MessageType Header => MessageType.StatusRequest;
    public uint Random;

    public void Serialize(Stream s) => StreamIO.WriteUInt32(s, this.Random);
    public void Deserialize(Stream s) => this.Random = StreamIO.ReadUInt32(s);
}
