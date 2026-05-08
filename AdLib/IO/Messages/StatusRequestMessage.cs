namespace AdLib.IO.Messages;

public struct StatusRequestMessage : IMessage
{
    public MessageType Header => MessageType.StatusRequest;
    public uint Random;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteUInt32(s, this.Random);
    public void Deserialize(System.IO.Stream s) => this.Random = BitUtils.ReadUInt32(s);
}
