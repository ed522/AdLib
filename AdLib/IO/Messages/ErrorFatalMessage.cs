using System.IO;

namespace AdLib.IO.Messages;

public struct ErrorFatalMessage : IMessage
{
    public MessageType Header => MessageType.ErrorFatal;
    public FatalError Errno;

    public void Serialize(Stream s) => StreamIO.WriteUInt32(s, (uint)this.Errno);
    public void Deserialize(Stream s) => this.Errno = (FatalError)StreamIO.ReadUInt32(s);
}
