using System.Security.Cryptography;

using Microsoft.DevTunnels.Ssh.Algorithms;

namespace AdLib.Cryptography;

public static class PublicKeyExtensions
{
    public static byte[] GetThumbprint(this IKeyPair buffer, HashAlgorithmName hashAlgorithm) =>
        CryptographicOperations.HashData(hashAlgorithm, buffer.GetPublicKeyBytes().Span);
}