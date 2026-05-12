using System;

namespace AdLib.IO;

public class InvalidPathException(string msg) : Exception(msg)
{
    public enum InvalidPathReason
    {
        Unspecified = 0,
        CaseConflict,
        ForbiddenPath,
        InvalidCharacters,
    }

    public InvalidPathException(InvalidPathReason reason) : this(GetReasonString(reason)) =>
        this.Reason = reason;

    public InvalidPathReason Reason { get; }

    private static string GetReasonString(InvalidPathReason reason)
    {
        return reason switch
        {
            InvalidPathReason.CaseConflict => "In a case-insensitive context, the path is distinguished " +
                                              "from an already-existing file only by case differences.",
            InvalidPathReason.ForbiddenPath => "The given path is forbidden.",
            InvalidPathReason.InvalidCharacters => "The given path contains characters that are invalid on " +
                                                   "the target filesystem or operating system.",
            InvalidPathReason.Unspecified => "Unspecified reason.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
    }
}
