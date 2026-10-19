using System;

namespace AdLib.IO.Messages;

public class FileRequestMessage : IMessage
{
    public MessageType Header => MessageType.FileRequest;
    public string Path;

    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[BufferIO.GetStringLength(this.Path)];
        BufferIO.WriteString(buffer, this.Path, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer) { this.Path = BufferIO.ReadString(buffer, out _); }
}
