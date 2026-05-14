using System;
using System.Security.Cryptography.X509Certificates;

using AdLib.Cryptography;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.EdEC;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

using X509Certificate = Org.BouncyCastle.X509.X509Certificate;
using ClrX509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate;

namespace AdLib.Identities;

public class Identity
{
    /// <summary>
    ///     The date where certificates will expire. This is a long time in the future, since certificates
    ///     don't need to expire (no real security benefit).
    /// </summary>
    private static readonly DateTime ExpiryDate = new(9999, 12, 31);

    private Identity(
        X509Certificate cert, AsymmetricKeyParameter privateKey, string friendlyName, string internalName
    )
    {
        this.Cert = cert;
        this.ClrCert = X509CertificateLoader.LoadCertificate(cert.GetEncoded());
        this.PrivateKey = privateKey;
        this.FriendlyName = friendlyName;
        this.InternalName = internalName;
    }

    public Identity(IdentityMetadata metadata, string internalName, char[] password)
    {
        this.InternalName = internalName;

        if (metadata == null)
        {
            throw new InvalidOperationException("Failed to deserialize identity");
        }

        this.Cert = new X509CertificateParser().ReadCertificate(metadata.Certificate);
        this.ClrCert = X509CertificateLoader.LoadCertificate(metadata.Certificate);

        Lockbox box = Lockbox.DecryptLockbox(metadata.EncryptedPrivateKey, metadata.Certificate, password);

        this.PrivateKey = PrivateKeyFactory.CreateKey(
            new PrivateKeyInfo(
                new AlgorithmIdentifier(EdECObjectIdentifiers.id_Ed448),
                Asn1Object.FromByteArray(box.Data)
            )
        );

        this.FriendlyName = metadata.FriendlyName;
    }

    public X509Certificate Cert { get; }
    public ClrX509Certificate ClrCert { get; }
    public AsymmetricKeyParameter PrivateKey { get; }
    public string InternalName { get; }
    public string FriendlyName { get; }

    public static Identity LoadFromFile(string storePath, string internalName, char[] password)
    {
        IdentityMetadata metadata = IdentityMetadata.LoadMetadata(storePath, internalName);
        return new Identity(metadata, internalName, password);
    }

    public static Identity CreateNew(
        string storePath, string friendlyName, char[] password, bool isClient
    )
    {
        if (friendlyName.Contains('='))
        {
            throw new ArgumentException("Friendly name must not contain '='");
        }

        ECKeyPairGenerator generator = new();
        generator.Init(new Ed448KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair keyPair = generator.GenerateKeyPair();

        // make cert
        X509V3CertificateGenerator certGenerator = new();
        certGenerator.SetPublicKey(keyPair.Public);
        certGenerator.SetSubjectDN(new X509Name("CN=" + friendlyName)); // only set CN
        certGenerator.SetIssuerDN(new X509Name("CN=" + friendlyName));
        certGenerator.SetNotBefore(DateTime.Now);
        certGenerator.SetNotAfter(ExpiryDate);
        // random bc there's no way to reliably generate sequential serials
        certGenerator.SetSerialNumber(new BigInteger(192, new SecureRandom()));

        certGenerator.AddExtension(X509Extensions.BasicConstraints, true,
            new BasicConstraints(false));

        certGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true,
            new ExtendedKeyUsage(isClient ? KeyPurposeID.id_kp_clientAuth : KeyPurposeID.id_kp_serverAuth));

        // self-signed
        ISignatureFactory signatureFactory = new Asn1SignatureFactory("Ed448", keyPair.Private);
        X509Certificate bcCert = certGenerator.Generate(signatureFactory);

        Lockbox box = Lockbox.Create();
        box.Data = PrivateKeyInfoFactory.CreatePrivateKeyInfo(keyPair.Private).GetEncoded();
        byte[] encryptedPrivateKey = box.EncryptNewLockbox(password, bcCert.GetEncoded());

        IdentityMetadata metadata = new()
        {
            Certificate = bcCert.GetEncoded(),
            EncryptedPrivateKey = encryptedPrivateKey,
            FriendlyName = friendlyName,
        };

        string internalName = metadata.GetSanitizedFileName();
        metadata.WriteMetadata(storePath, internalName);

        return new Identity(bcCert, keyPair.Private, friendlyName, internalName);
    }
}
