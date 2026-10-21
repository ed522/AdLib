using System;

namespace AdLib.IO.Messages;

public class FileRequestMessage : IMessage
{
    public MessageType Header => MessageType.FileRequest;
    public string RemotePath;
    public string DestinationPath;

    public Span<byte> Serialize()
    {
        Span<byte> buffer = new byte[BufferIO.GetStringLength(this.RemotePath) +
                                     BufferIO.GetStringLength(this.DestinationPath)];

        BufferIO.WriteString(buffer, this.RemotePath, out int len);
        BufferIO.WriteString(buffer[len..], this.DestinationPath, out _);
        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.RemotePath = BufferIO.ReadString(buffer, out int len);
        this.DestinationPath = BufferIO.ReadString(buffer[len..], out _);
    }
}
