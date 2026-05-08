namespace AdLib.IO.Messages;

public struct ErrorRecoverableMessage : IMessage
{
    public MessageType Header => MessageType.ErrorRecoverable;
    public uint Errno;

    public void Serialize(System.IO.Stream s) => BitUtils.WriteUInt32(s, this.Errno);
    public void Deserialize(System.IO.Stream s) => this.Errno = BitUtils.ReadUInt32(s);
}
