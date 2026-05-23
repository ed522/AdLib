using System.Text.Json.Serialization;

namespace AdLib.Identities;

[JsonSerializable(typeof(Certificate)), JsonSerializable(typeof(IdentityMetadata))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
