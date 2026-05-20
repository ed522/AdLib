using System.Diagnostics;
using System.Security.Cryptography;

using Org.BouncyCastle.X509;

namespace AdLib.Cryptography;

public static class BCX509CertificateExtensions
{
    public static byte[] GetSha3Fingerprint(this X509Certificate cert)
    {
        byte[] encoded = cert.GetEncoded();
        byte[] result = SHA3_256.HashData(encoded);
        Debug.Assert(result.Length == 32);
        return result;
    }
}
