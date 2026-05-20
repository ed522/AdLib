using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace AdLib.Identities;

public record Certificate
{
    private static readonly JsonSerializerOptions OPTIONS = new();

    static Certificate()
    {
        OPTIONS.Converters.Add(new CertificateJsonConverter());
    }
    
    public const string FILE_EXTENSION = ".adc";
    public required string InternalName { get; init; }
    public required string FriendlyName { get; init; }
    public required X509Certificate2 X509Cert { get; init; }
    
    public static Certificate LoadCertificate(string path)
    {
        byte[] json = File.ReadAllBytes(path);
        return LoadCertificate(json, path);
    }

    public static Certificate LoadCertificate(byte[] bytes, string? path = null) =>
        JsonSerializer.Deserialize<Certificate>(Encoding.UTF8.GetString(bytes), OPTIONS) ??
        throw new InvalidOperationException($"Corrupted certificate {path}: got null");

    public byte[] SerializeCertificate()
    {
        string jsonString = JsonSerializer.Serialize(this, OPTIONS);
        return Encoding.UTF8.GetBytes(jsonString);
    }
}
