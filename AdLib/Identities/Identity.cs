using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using AdLib.Cryptography;

namespace AdLib.Identities;

public class Identity
{
    /// <summary>
    ///     The date where certificates will expire. This is a long time in the future, since certificates
    ///     don't need to expire (no real security benefit).
    /// </summary>
    private static readonly DateTime ExpiryDate = new(9999, 12, 31);

    private static readonly HashAlgorithmName CertHash = HashAlgorithmName.SHA3_256;
    private static readonly ECCurve StandardCurve = ECCurve.NamedCurves.nistP256;

    internal static string GetFileName(Guid internalName) =>
        internalName.ToString("D") + IdentityMetadata.FILE_EXTENSION;

    private Identity(
        ECDsa keys, X509Certificate2 cert, string friendlyName, Guid internalName
    )
    {
        this.Keys = keys;
        this.Cert = cert;
        this.FriendlyName = friendlyName;
        this.InternalName = internalName;
    }

    public Identity(IdentityMetadata metadata, char[] password)
    {
        if (metadata == null)
        {
            throw new InvalidOperationException("Failed to deserialize identity");
        }

        this.InternalName = metadata.InternalName;
        Lockbox box = Lockbox.DecryptLockbox(metadata.EncryptedPrivateKey, metadata.CertificatePfx, password);

        this.Keys = ECDsa.Create();
        this.Keys.ImportPkcs8PrivateKey(box.Data, out _);

        if (Environment.OSVersion.Platform is
            PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE)
        {
            string pass = new(password);

            this.Cert = X509CertificateLoader.LoadPkcs12Collection(
                metadata.CertificatePfx,
                pass,
                X509KeyStorageFlags.MachineKeySet
            )[0];
        }
        else
        {
            string pass = new(password);

            this.Cert = X509CertificateLoader.LoadPkcs12Collection(
                metadata.CertificatePfx,
                pass,
                X509KeyStorageFlags.EphemeralKeySet
            )[0];
        }

        this.Cert = this.Cert.CopyWithPrivateKey(this.Keys);

        this.FriendlyName = metadata.FriendlyName;
    }

    public X509Certificate2 Cert { get; }
    public ECDsa Keys { get; }
    public Guid InternalName { get; }
    public string FriendlyName { get; }

    public static Identity LoadFromFile(string storePath, Guid internalName, char[] password)
    {
        IdentityMetadata metadata = IdentityMetadata.LoadMetadata(storePath, GetFileName(internalName));

        if (internalName != metadata.InternalName)
        {
            throw new InvalidOperationException($"Tried to load identity {internalName} but instead found " +
                                                $"{metadata.InternalName} at its expected location");
        }

        return new Identity(metadata, password);
    }

    public static Identity CreateNew(string storePath, string friendlyName, char[] password) =>
        CreateNew(storePath, friendlyName, password, out _);

    internal static Identity CreateNew(
        string storePath, string friendlyName, char[] password, out IdentityMetadata metadata
    )
    {
        if (friendlyName.Contains('='))
        {
            throw new ArgumentException("Friendly name must not contain '='");
        }

        ECDsa ecdsa = ECDsa.Create(StandardCurve);
        CertificateRequest certRequest = new("CN=" + friendlyName, ecdsa, CertHash);
        certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        X509Certificate2 cert = certRequest.CreateSelfSigned(DateTime.UtcNow, ExpiryDate);

        // get rid of private key so it doesn't get leaked
        byte[] certBytes = cert.Export(X509ContentType.Cert);
        X509Certificate2 strippedCert = X509CertificateLoader.LoadCertificate(certBytes);
        Debug.Assert(!strippedCert.HasPrivateKey);

        Guid internalName = GetCertificateGuid(cert.GetCertHash(CertHash));

        byte[] export = strippedCert.Export(X509ContentType.Pfx);
        Lockbox box = Lockbox.Create();
        box.Data = ecdsa.ExportPkcs8PrivateKey();
        byte[] encryptedPrivateKey = box.EncryptNewLockbox(password, export);

        metadata = new IdentityMetadata
        {
            CertificatePfx = export,
            EncryptedPrivateKey = encryptedPrivateKey,
            FriendlyName = friendlyName,
            InternalName = internalName,
        };

        metadata.WriteMetadata(storePath, GetFileName(internalName));

        return new Identity(ecdsa, cert, friendlyName, internalName);
    }

    public static Guid GetCertificateGuid(byte[] hash)
    {
        byte[] guid = hash[..16];
        // UUID format is xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx
        // where y = 0b1000, 0b1001, 0b1010, 0b1011 (8 9 a b)
        // uuid[7] is 4x
        // uuid[8] is yx

        // version (4 in binary)
        guid[7] &= 0b0000_1111;
        guid[7] |= 0b0100_0000;
        // variant (10 is variant 1, which has 1 entire extra bit)
        guid[8] &= 0b00_111111;
        guid[8] |= 0b10_000000;
        return new Guid(guid);
    }

    public override bool Equals(object? other) =>
        other is Identity ident &&
        this.InternalName == ident.InternalName &&
        this.FriendlyName == ident.FriendlyName &&
        this.Cert.Equals(ident.Cert);

    // leaves out Ecdsa since that has no equality operator
    public override int GetHashCode() =>
        HashCode.Combine(this.InternalName, this.FriendlyName, this.Cert);
}