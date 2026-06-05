namespace AdLib.Identities;

public record HostPublicKeyInfo
{
    public required string Host { get; init; }
    public required PublicKeyInfo PublicKeyInfo { get; init; }
}
