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

        BitUtils.WriteString(stream, this.Path);
        BitUtils.WriteVarInt(stream, (ulong)this.Files.Length);
        foreach (FileEntry file in this.Files)
        {
            stream.WriteByte(file.IsDirectory ? (byte)1 : (byte)0);
            BitUtils.WriteString(stream, file.Name);
        }
    }

    public void Deserialize(Stream stream)
    {
        this.Path = BitUtils.ReadString(stream);
        ulong length = BitUtils.ReadVarInt(stream);
        this.Files = new FileEntry[length];
        for (uint i = 0; i < length; i++)
        {
            bool isDirectory = stream.ReadByte() == 1;
            string name = BitUtils.ReadString(stream);
            this.Files[i] = new FileEntry { Name = name, IsDirectory = isDirectory };
        }
    }
}

public struct FileEntry
{
    public string Name;
    public bool IsDirectory;
}