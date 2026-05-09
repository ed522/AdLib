using System.IO;

namespace AdLib.IO.Messages;

public struct ErrorFatalMessage : IMessage
{
    public MessageType Header => MessageType.ErrorFatal;
    public uint Errno;

    public void Serialize(Stream s) => StreamIO.WriteUInt32(s, this.Errno);
    public void Deserialize(Stream s) => this.Errno = StreamIO.ReadUInt32(s);
}
