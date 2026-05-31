using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdLib.Identities;

public record Certificate
{
    public const string FILE_EXTENSION = ".adc";
    public required Guid InternalName { get; init; }
    public required string FriendlyName { get; init; }

    // weird conversion logic since the source generator will always crawl public types, even if they have a custom 
    // converter, which means it tries to access a bunch of random obsolete fields
    // workaround: encode it ourselves and hide the real cert
    [JsonIgnore]
    public required X509Certificate2 X509Cert { get; init; }

    [JsonInclude, JsonPropertyName("X509Cert")]
    internal byte[] RawCertData
    {
        get => this.X509Cert.Export(X509ContentType.Cert);
        init => this.X509Cert = X509CertificateLoader.LoadCertificate(value);
    }

    public static Certificate LoadCertificate(string path)
    {
        byte[] json = File.ReadAllBytes(path);
        return LoadCertificate(json, path);
    }

    public static Certificate LoadCertificate(byte[] bytes, string? path = null) =>
        JsonSerializer.Deserialize(bytes, SourceGenerationContext.Default.Certificate) ??
        throw new InvalidOperationException($"Corrupted certificate {path}: got null");

    public byte[] SerializeCertificate() => JsonSerializer.SerializeToUtf8Bytes(this, SourceGenerationContext.Default.Certificate);
}
