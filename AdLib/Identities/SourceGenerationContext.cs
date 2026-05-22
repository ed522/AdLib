using System.Text.Json.Serialization;

namespace AdLib.Identities;

[JsonSourceGenerationOptions(WriteIndented = true), JsonSerializable(typeof(Certificate)),
 JsonSerializable(typeof(IdentityMetadata))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
