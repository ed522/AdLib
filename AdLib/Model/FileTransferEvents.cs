using System;

namespace AdLib.Model;

public class ConnectedEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
}

public class FileReceivedEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
    public required string Path { get; init; }
}

public class FileSentEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
    public required string Path { get; init; }
}

public class DisconnectedEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
    public string? Reason { get; init; }
}

public class FatalErrorOccurredEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
    public required string Message { get; init; }
    public Exception? CausingException { get; init; }
}

public class RecoverableErrorOccurredEventArgs : EventArgs
{
    public ClientInfo? Client { get; set; }
    public required string Message { get; init; }
    public Exception? CausingException { get; init; }
}
