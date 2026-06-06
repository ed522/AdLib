using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.DevTunnels.Ssh.Algorithms;

namespace AdLib.Identities;

public sealed class PublicKeyInfo
{
    public const string FileExtension = ".adc";
    private static readonly HashAlgorithm HashAlgorithm = SHA256.Create();
    public static int FingerprintLength => HashAlgorithm.HashSize / 8;

    public required Guid InternalName { get; init; }
    public required string FriendlyName { get; init; }
    public required byte[] PublicKeyFingerprint { get; init; }

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

    public static byte[] GetCanonicalFingerprint(IKeyPair key)
    {
        byte[] data = new byte[32];
        GetCanonicalFingerprint(key, data);
        return data;
    }

    public static void GetCanonicalFingerprint(IKeyPair key, Span<byte> output)
    {
        HashAlgorithm.TryComputeHash(key.GetPublicKeyBytes().Span, output, out _);
    }

    public bool Equals(PublicKeyInfo? other)
    {
        if (other is null) return false;

        return this.InternalName == other.InternalName &&
               this.FriendlyName == other.FriendlyName &&
               this.PublicKeyFingerprint.AsSpan().SequenceEqual(other.PublicKeyFingerprint.AsSpan());
    }

    public override int GetHashCode() =>
        HashCode.Combine(this.InternalName, this.FriendlyName, this.PublicKeyFingerprint);

    public override string ToString() => $"{this.FriendlyName} [{this.InternalName}]";
}