using System;

namespace AdLib.IO.Messages;

public class DeleteMessage : IMessage
{
    public MessageType Header => MessageType.Delete;
    public string Path;

    public Span<byte> Serialize()
    {
        Span<byte> buffer = new byte[BufferIO.GetStringLength(this.Path)];
        BufferIO.WriteString(buffer, this.Path, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer) => this.Path = BufferIO.ReadString(buffer, out _);
}
