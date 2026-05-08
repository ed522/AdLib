using System;

namespace AdLib.IO.Messages;

public struct DataMessage : IMessage
{
    public MessageType Header => MessageType.Data;
    public string Path;
    public byte[] Data;
    public uint Crc32;

    public void Serialize(System.IO.Stream s)
    {
        BitUtils.WriteString(s, this.Path);
        BitUtils.WriteBlock(s, this.Data ?? 
                               throw new InvalidOperationException("cannot write empty data block"));
        BitUtils.WriteUInt32(s, this.Crc32);
    }

    public void Deserialize(System.IO.Stream s)
    {
        this.Path = BitUtils.ReadString(s);
        this.Data = BitUtils.ReadBlock(s);
        this.Crc32 = BitUtils.ReadUInt32(s);
    }
}
