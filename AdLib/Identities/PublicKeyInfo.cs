using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;

namespace AdLib.Identities;

public record PublicKeyInfo
{
    public const string FILE_EXTENSION = ".adc";
    public required Guid InternalName { get; init; }
    public required string FriendlyName { get; init; }

    // weird conversion logic since the source generator will always crawl public types, even if they have a custom 
    // converter, which means it tries to access a bunch of random obsolete fields
    // workaround: encode it ourselves and hide the real cert
    [JsonIgnore] public required IKeyPair PublicKey { get; init; }

    [JsonInclude, JsonPropertyName(nameof(PublicKey))]
    internal byte[] RawCertData
    {
        get => this.PublicKey.GetPublicKeyBytes().ToArray();
        init => this.PublicKey = KeyPair.ImportKeyBytes(value);
    }

    public static PublicKeyInfo LoadPublicKeyInfo(string path)
    {
        byte[] json = File.ReadAllBytes(path);
        return LoadPublicKeyInfo(json, path);
    }

    public static PublicKeyInfo LoadPublicKeyInfo(byte[] bytes, string? path = null) =>
        JsonSerializer.Deserialize(bytes, SourceGenerationContext.Default.PublicKeyInfo) ??
        throw new InvalidOperationException($"Corrupted certificate {path}: got null");

    public byte[] SerializePublicKeyInfo() =>
        JsonSerializer.SerializeToUtf8Bytes(this, SourceGenerationContext.Default.PublicKeyInfo);
}