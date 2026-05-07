namespace AdLib.IO.Message;

public interface IMessage
{
    
    byte Header { get; }
    void Serialize(System.IO.Stream stream);
    void Deserialize(System.IO.Stream stream);
    
    // set highest bit to indicate client-to-server 
    
    // formats:
    // string: varint length, n bytes data
    // block: 4 byte length, n bytes data
    
    // no extra data (for now)
    const byte FRAME_INIT = 0x01;
    // ^
    const byte FRAME_INIT_ACK = 0x81;
    
    // data block, 4 byte CRC32
    const byte FRAME_DATA = 0x10;
    
    // 4 bytes follow (random)
    const byte FRAME_STATUS_REQUEST = 0x20;
    // the 4 bytes from the request
    const byte FRAME_STATUS_RESPONSE = 0xA0;
    // string for path
    const byte FRAME_CHANGE_TARGET = 0x21;
    // string for path
    const byte FRAME_MAKE_DIR = 0x22;
    // string for path
    const byte FRAME_DELETE = 0x23;
    // string for path, 32 bytes for expected hash (BLAKE3)
    const byte FRAME_HASH_CHECK = 0x24;
    // byte for status (0/1, correct/update needed)
    const byte FRAME_HASH_RESPONSE = 0xA4;
    
    // followed by target control code (i.e. what the ACK applies to), 1 byte
    const byte FRAME_CONTROL_ACK = 0xAF;
    
    // (both) no extra data
    const byte FRAME_END = 0x40;
    const byte FRAME_END_ACK = 0x41;
    
    // (both) 4-byte errno
    const byte FRAME_ERROR_RECOVERABLE = 0xFE;
    const byte FRAME_ERROR_FATAL = 0xFF;
}
