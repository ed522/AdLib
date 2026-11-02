using System;

namespace AdLib.IO.Messages;

public class HashCheckMessage : IMessage
{
    public MessageType Header => MessageType.HashCheck;
    public string Path;
    public byte[] ExpectedHash; // 32 bytes

    private const int HashLength = 32;


    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[BufferIO.GetStringLength(this.Path) + 32];
        this.ExpectedHash.CopyTo(buffer.AsSpan()[..HashLength]);
        BufferIO.WriteString(buffer.AsSpan()[HashLength..], this.Path, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.ExpectedHash = buffer[..HashLength].ToArray();
        this.Path = BufferIO.ReadString(buffer[HashLength..], out _);
    }
}
