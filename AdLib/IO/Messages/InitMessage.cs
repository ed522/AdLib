using System.IO;
using System.Text;

namespace AdLib.IO.Messages;

public struct InitMessage : IMessage
{
    public MessageType Header => MessageType.Init;
    public char PathSeparator;

    public void Serialize(Stream stream) =>
        StreamIO.WriteSmallBlock(stream, Encoding.UTF8.GetBytes(this.PathSeparator.ToString()));

    public void Deserialize(Stream stream) =>
        this.PathSeparator = Encoding.UTF8.GetChars(StreamIO.ReadSmallBlock(stream))[0];
}
