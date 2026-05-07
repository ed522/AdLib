namespace AdLib.IO.Message;

public struct StatusRequestMessage : IMessage
{
    public byte Header => IMessage.FRAME_STATUS_REQUEST;
    public uint Random;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteUInt32(s, this.Random);
    public void Deserialize(System.IO.Stream s) => this.Random = BitUtils.ReadUInt32(s);
}
