using System.IO;
using System.Text;

namespace AdLib.IO.Messages;

public struct InitAckMessage : IMessage
{
    public MessageType Header => MessageType.InitAck;
    public string SharedFolderPath;
    public char PathSeparator;

    public void Serialize(Stream stream)
    {
        StreamIO.WriteString(stream, this.SharedFolderPath);
        StreamIO.WriteSmallBlock(stream, Encoding.UTF8.GetBytes(this.PathSeparator.ToString()));
    }

    public void Deserialize(Stream stream)
    {
        this.SharedFolderPath = StreamIO.ReadString(stream);
        this.PathSeparator = Encoding.UTF8.GetChars(StreamIO.ReadSmallBlock(stream))[0];
    }
}
