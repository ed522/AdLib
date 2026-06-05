using System.Text.Json.Serialization;

namespace AdLib.Identities;

[JsonSerializable(typeof(PublicKeyInfo)), JsonSerializable(typeof(IdentityMetadata))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
