using System;

namespace AdLib.IO.Messages;

public class ListFilesResponseMessage : IMessage
{
    public MessageType Header => MessageType.ListFilesResponse;
    public string Path;
    public FileEntry[] Files;

    public Span<byte> Serialize()
    {
        if (this.Files is null)
        {
            throw new InvalidOperationException("Incomplete message");
        }

        int length = BufferIO.GetStringLength(this.Path) + sizeof(uint);

        foreach (FileEntry file in this.Files)
        {
            length += BufferIO.GetStringLength(file.Name) + 1;
        }

        Span<byte> buffer = new byte[length];
        int index = 0;

        BufferIO.WriteString(buffer[index..], this.Path, out int i);
        index += i;
        index += BufferIO.WriteVarInt((uint)this.Files.Length, buffer[index..]);

        foreach (FileEntry file in this.Files)
        {
            buffer[index++] = (byte)(file.IsDirectory ? 1 : 0);
            BufferIO.WriteString(buffer[index..], file.Name, out i);
            index += i;
        }

        return buffer;
    }

    public void Deserialize(ReadOnlySpan<byte> buffer)
    {
        this.Path = BufferIO.ReadString(buffer, out int index);
        uint fileCount = (uint)BufferIO.ReadVarInt(buffer[index..], out int i);
        index += i;
        this.Files = new FileEntry[fileCount];

        for (uint j = 0; j < fileCount; j++)
        {
            this.Files[j] = new FileEntry
            {
                Name = BufferIO.ReadString(buffer[index..], out i),
                IsDirectory = buffer[index++] != 0,
            };
        }
    }
}

public struct FileEntry
{
    public string Name;
    public bool IsDirectory;
}