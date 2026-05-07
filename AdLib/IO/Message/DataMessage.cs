using System;

namespace AdLib.IO.Message;

public struct DataMessage : IMessage
{
    public byte Header => IMessage.FRAME_DATA;
    public byte[] Data;
    public uint Crc32;

    public void Serialize(System.IO.Stream s)
    {
        BitUtils.WriteBlock(s, this.Data ?? 
                               throw new InvalidOperationException("cannot write empty data block"));
        BitUtils.WriteUInt32(s, this.Crc32);
    }

    public void Deserialize(System.IO.Stream s)
    {
        this.Data = BitUtils.ReadBlock(s);
        this.Crc32 = BitUtils.ReadUInt32(s);
    }
}
