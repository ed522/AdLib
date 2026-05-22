using System;
using System.IO;
using System.Text.Json;

namespace AdLib.Identities;

public class IdentityMetadata
{
    public const string FILE_EXTENSION = ".adi";

    public required Guid InternalName { get; init; }
    public required byte[] Certificate { get; init; }
    public required byte[] EncryptedPrivateKey { get; init; }
    public required string FriendlyName { get; init; }

    public static IdentityMetadata LoadMetadata(string storePath, string fileName)
    {
        string fullPath = Path.Combine(storePath, fileName + FILE_EXTENSION);
        byte[] jsonBytes = File.ReadAllBytes(fullPath);

        return JsonSerializer.Deserialize(jsonBytes, SourceGenerationContext.Default.IdentityMetadata) ??
               throw new InvalidOperationException($"Corrupted identity {fullPath}: got null");
    }

    public void WriteMetadata(string storePath, string fileName)
    {
        string fullPath = Path.Combine(storePath, fileName + FILE_EXTENSION);
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(this, SourceGenerationContext.Default.IdentityMetadata);
        File.WriteAllBytes(fullPath, jsonBytes);
    }

    public string GetSanitizedFileName() =>
        this.FriendlyName.Replace('/', '_')
            .Replace(':', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace('"', '_')
            .Replace('*', '_')
            .Replace('|', '_')
            .Replace('?', '_')
            .Replace('\\', '_');
}
