namespace AdLib.Identities;

public record HostCertificate
{
    public required string Host { get; init; }
    public required Certificate Certificate { get; init; }
}
