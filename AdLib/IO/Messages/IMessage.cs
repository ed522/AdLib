using System.IO;

namespace AdLib.IO.Messages;

public enum MessageType : byte
{
    Init = 0x01,
    InitAck = 0x81,
    Data = 0x10,
    DataFinished = 0x11,
    FileRequest = 0x12,
    StatusRequest = 0x20,
    StatusResponse = 0xA0,
    ListFiles = 0x21,
    ListFilesResponse = 0xA1,
    MakeDir = 0x22,
    Delete = 0x23,
    HashCheck = 0x24,
    HashResponse = 0xA4,
    ControlAck = 0xAF,
    End = 0x40,
    EndAck = 0x41,
    ErrorRecoverable = 0xFE,
    ErrorFatal = 0xFF,
}

public interface IMessage
{
    MessageType Header { get; }
    void Serialize(Stream stream);
    void Deserialize(Stream stream);

    // formats:
    // string: varint length, n bytes data
    // block: 4 byte length, n bytes data
}
