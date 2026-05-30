namespace AdLib.Model;

public struct ClientRequest
{
    public required ClientRequestType Type;
    public required string Path;
}

public enum ClientRequestType
{
    PutPath,
    GetPath,
    DeleteRemotePath,
    MakeRemoteDir,
    ListFiles,
    Disconnect,
}
