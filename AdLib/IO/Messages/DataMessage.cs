using System;

namespace AdLib.IO.Messages;

public class DataMessage : IMessage
{
    public MessageType Header => MessageType.Data;
    public string? Path;
    public byte[]? Data;
    public ulong Offset;
    public uint Crc32;

    public Span<byte> Serialize()
    {
        if (this.Data is null || this.Path is null)
        {
            throw new InvalidOperationException("cannot write empty data block");
        }

        int length = BufferIO.GetStringLength(this.Path) + sizeof(ulong) + BufferIO.GetBlockLength(this.Data) +
                     sizeof(uint);
        Span<byte> buffer = new byte[length];
        int index = 0;

        BufferIO.WriteString(buffer[index..], this.Path, out int i);
        index += i;
        BufferIO.WriteUInt64(buffer[index..], this.Offset, out i);
        index += i;
        BufferIO.WriteBlock(buffer[index..], this.Data, out i);
        index += i;
        BufferIO.WriteUInt32(buffer[index..], this.Crc32, out _);

        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        int index = 0;

        this.Path = BufferIO.ReadString(buffer[index..], out int i);
        index += i;
        this.Offset = BufferIO.ReadUInt64(buffer[index..], out i);
        index += i;
        this.Data = BufferIO.ReadBlock(buffer[index..], out i).ToArray();
        index += i;
        this.Crc32 = BufferIO.ReadUInt32(buffer[index..], out _);
    }
}
