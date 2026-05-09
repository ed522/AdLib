using System.IO;

namespace AdLib.IO.Messages;

public struct HashResponseMessage : IMessage
{
    public MessageType Header => MessageType.HashResponse;
    public bool Status; // 0/1, correct/update needed

    public void Serialize(Stream s) => s.WriteByte(this.Status ? (byte)1 : (byte)0);

    public void Deserialize(Stream s)
    {
        int result = s.ReadByte();
        if (result == -1) throw new EndOfStreamException();
        this.Status = result != 0;
    }
}
