using System.IO;

namespace AdLib.IO.Messages;

public struct ErrorFatalMessage : IMessage
{
    public MessageType Header => MessageType.ErrorFatal;
    public uint Errno;

    public void Serialize(Stream s) => BitUtils.WriteUInt32(s, this.Errno);
    public void Deserialize(Stream s) => this.Errno = BitUtils.ReadUInt32(s);
}
