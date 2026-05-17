using System;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdLib.Identities;

public class CertificateJsonConverter : JsonConverter<X509Certificate2>
{
    public override X509Certificate2 Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Invalid token: expected string, got " + reader.TokenType);
        }

        string? base64EncodedCert = reader.GetString();

        if (base64EncodedCert is null)
        {
            throw new JsonException("Invalid token: expected string, got null");
        }

        try
        {
            byte[] certBytes = Convert.FromBase64String(base64EncodedCert);
            return X509CertificateLoader.LoadCertificate(certBytes);
        }
        catch (Exception ex)
        {
            throw new JsonException("Failed to convert Base64 string to X509Certificate.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, X509Certificate2 value, JsonSerializerOptions options)
    {
        byte[] certBytes = value.Export(X509ContentType.Cert);
        string base64EncodedCert = Convert.ToBase64String(certBytes);
        writer.WriteStringValue(base64EncodedCert);
    }
}
