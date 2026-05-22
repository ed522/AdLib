using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdLib.Identities;

public record Certificate
{
    public const string FILE_EXTENSION = ".adc";
    public required string InternalName { get; init; }
    public required string FriendlyName { get; init; }

    [JsonConverter(typeof(CertificateJsonConverter))]
    public required X509Certificate2 X509Cert { get; init; }
    
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
