using System;
using System.IO;

namespace AdLib.IO.Messages;

public struct DataMessage : IMessage
{
    public MessageType Header => MessageType.Data;
    public string Path;
    public byte[] Data;
    public uint Crc32;

    public void Serialize(Stream s)
    {
        StreamIO.WriteString(s, this.Path);

        StreamIO.WriteBlock(s, this.Data ??
                               throw new InvalidOperationException("cannot write empty data block"));

        StreamIO.WriteUInt32(s, this.Crc32);
    }

    public void Deserialize(Stream s)
    {
        this.Path = StreamIO.ReadString(s);
        this.Data = StreamIO.ReadBlock(s);
        this.Crc32 = StreamIO.ReadUInt32(s);
    }
}
