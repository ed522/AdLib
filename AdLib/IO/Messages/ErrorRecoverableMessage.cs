using System.IO;

namespace AdLib.IO.Messages;

public struct ErrorRecoverableMessage : IMessage
{
    public MessageType Header => MessageType.ErrorRecoverable;
    public RecoverableError Errno;

    public void Serialize(Stream s) => StreamIO.WriteUInt32(s, (uint)this.Errno);
    public void Deserialize(Stream s) => this.Errno = (RecoverableError)StreamIO.ReadUInt32(s);
}
