using System;
using System.IO;

namespace AdLib.IO.Messages;

public struct ListFilesResponseMessage : IMessage
{
    public MessageType Header => MessageType.ListFilesResponse;
    public string Path;
    public FileEntry[] Files;

    public void Serialize(Stream stream)
    {
        if (this.Files is null)
        {
            throw new InvalidOperationException("Incomplete message");
        }

        StreamIO.WriteString(stream, this.Path);
        StreamIO.WriteVarInt(stream, (ulong)this.Files.Length);

        foreach (FileEntry file in this.Files)
        {
            stream.WriteByte(file.IsDirectory ? (byte)1 : (byte)0);
            StreamIO.WriteString(stream, file.Name);
        }
    }

    public void Deserialize(Stream stream)
    {
        this.Path = StreamIO.ReadString(stream);
        ulong length = StreamIO.ReadVarInt(stream);
        this.Files = new FileEntry[length];

        for (uint i = 0; i < length; i++)
        {
            bool isDirectory = stream.ReadByte() == 1;
            string name = StreamIO.ReadString(stream);
            this.Files[i] = new FileEntry { Name = name, IsDirectory = isDirectory };
        }
    }
}

public struct FileEntry
{
    public string Name;
    public bool IsDirectory;
}
