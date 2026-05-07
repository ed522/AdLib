namespace AdLib.IO.Message;

public struct ErrorRecoverableMessage : IMessage
{
    public byte Header => IMessage.FRAME_ERROR_RECOVERABLE;
    public uint Errno;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteUInt32(s, this.Errno);
    public void Deserialize(System.IO.Stream s) => this.Errno = BitUtils.ReadUInt32(s);
}
