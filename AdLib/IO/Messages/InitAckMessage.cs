using System;

namespace AdLib.IO.Messages;

public class InitAckMessage : IMessage
{
    public MessageType Header => MessageType.InitAck;
    public string SharedFolderPath;
    public char PathSeparator;

    public Span<byte> Serialize()
    {
        Span<byte> buffer = new byte[BufferIO.GetStringLength(this.SharedFolderPath) + 1];
        buffer[0] = (byte)this.PathSeparator;
        BufferIO.WriteString(buffer[1..], this.SharedFolderPath, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.PathSeparator = (char)buffer[0];
        this.SharedFolderPath = BufferIO.ReadString(buffer[1..], out _);
    }
}
