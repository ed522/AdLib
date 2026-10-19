using System;

namespace AdLib.IO.Messages;

public class DataFinishedMessage : IMessage
{
    public MessageType Header => MessageType.DataFinished;
    public string Path;

    public Span<byte> Serialize()
    {
        byte[] buffer = new byte[BufferIO.GetStringLength(this.Path)];
        BufferIO.WriteString(buffer, this.Path, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer) { this.Path = BufferIO.ReadString(buffer, out _); }
}
