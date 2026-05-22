using System;
using System.IO;
using System.Text.Json;

namespace AdLib.Identities;

public class IdentityMetadata
{
    public const string FILE_EXTENSION = ".adi";

    public static readonly JsonSerializerOptions OPTIONS = new()
    {
        WriteIndented = true,
    };

    public required Guid InternalName { get; init; }
    public required byte[] Certificate { get; init; }
    public required byte[] EncryptedPrivateKey { get; init; }
    public required string FriendlyName { get; init; }

    public static IdentityMetadata LoadMetadata(string storePath, string fileName)
    {
        string fullPath = Path.Combine(storePath, fileName + FILE_EXTENSION);
        string jsonString = File.ReadAllText(fullPath);

        return JsonSerializer.Deserialize<IdentityMetadata>(jsonString, OPTIONS) ??
               throw new InvalidOperationException($"Corrupted identity {fullPath}: got null");
    }

    public void WriteMetadata(string storePath, string fileName)
    {
        string fullPath = Path.Combine(storePath, fileName + FILE_EXTENSION);
        string jsonString = JsonSerializer.Serialize(this, OPTIONS);
        File.WriteAllText(fullPath, jsonString);
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
