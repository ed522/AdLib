namespace AdLib.Model;

public struct ClientRequest
{
    public ClientRequestType Type;
    public string Path;
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
