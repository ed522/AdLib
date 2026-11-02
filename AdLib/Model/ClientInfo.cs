using AdLib.Identities;
using AdLib.IO;

namespace AdLib.Model;

/// <summary>
///     Holds information about a connected client.
/// </summary>
public sealed class ClientInfo
{
    public required string RemoteEndPoint { get; init; }
    public required PublicKeyInfo PublicKeyInfo { get; init; }
    public required IMessageChannel MessageChannel { get; init; }
}
