namespace AdLib.IO;

public enum FatalError : uint
{
    Unspecified = 0x0,

    IOError = 0x100,

    InvalidMessage = 0x200,
    MessageNotExpected = 0x201,
    InvalidAcknowledgement = 0x202,
}
