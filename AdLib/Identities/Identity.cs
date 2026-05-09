using System;
using System.Security.Cryptography.X509Certificates;

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.EdEC;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
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

    private static readonly Argon2BytesGenerator _hash = new();

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
            throw new InvalidOperationException("Failed to deserialize identity metadata.");
        }

        this.Cert = new X509CertificateParser().ReadCertificate(metadata.Certificate);
        this.ClrCert = X509CertificateLoader.LoadCertificate(metadata.Certificate);

        this.PrivateKey = DecryptPrivateKey(password, metadata.IV, metadata.PrivateKeySalt,
            metadata.EncryptedPrivateKey, this.Cert.GetEncoded());

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

        byte[] encryptedPrivateKey = DeriveAndEncryptPrivateKey(password, keyPair.Private,
            bcCert.GetEncoded(), out byte[] salt, out byte[] iv);

        IdentityMetadata metadata = new()
        {
            Certificate = bcCert.GetEncoded(),
            EncryptedPrivateKey = encryptedPrivateKey,
            IV = iv,
            PrivateKeySalt = salt,
            FriendlyName = friendlyName,
        };

        string internalName = metadata.GetSanitizedFileName();
        metadata.WriteMetadata(storePath, internalName);

        return new Identity(bcCert, keyPair.Private, friendlyName, internalName);
    }

    // above OWASP recommended min
    // Rec. from RFC: 2GiB is unusably high memory, 64MiB is not as bad but allocation is still too slow
    // on a Ryzen 5 5600, took ~200 ms per key derivation so slower machines should be capped around
    // ~500 ms, fastest around ~125 -- any more would get noticeable 
    private static Argon2Parameters MakeArgonParameters(byte[] salt) =>
        new Argon2Parameters.Builder()
            .WithSalt(salt)
            .WithParallelism(1)
            .WithMemoryAsKB(32768)
            .WithIterations(4)
            .WithVersion(Argon2Parameters.Version13)
            .Build();

    private static byte[] DeriveAndEncryptPrivateKey(
        char[] password, AsymmetricKeyParameter privateKey, byte[] certificate, out byte[] salt, out byte[] iv
    )
    {
        // custom encryption so that I can use a decent algorithm
        // derive key
        SecureRandom random = new();
        salt = new byte[32]; // matches key length
        random.NextBytes(salt);

        iv = new byte[12];
        Argon2Parameters argonParams = MakeArgonParameters(salt);

        _hash.Init(argonParams);
        byte[] secretKey = new byte[32];
        _hash.GenerateBytes(password, secretKey);

        byte[] privateKeyEncoded = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetEncoded();
        byte[] encryptedPrivateKey = new byte[privateKeyEncoded.Length + 16];

        ChaCha20Poly1305 cryptEngine = new();
        cryptEngine.Init(true, new AeadParameters(new KeyParameter(secretKey), 96, iv));

        // encrypt private key, but authenticate cert too
        cryptEngine.ProcessBytes(privateKeyEncoded, 0, privateKeyEncoded.Length,
            encryptedPrivateKey, 0);

        cryptEngine.ProcessAadBytes(certificate);
        cryptEngine.DoFinal(encryptedPrivateKey, encryptedPrivateKey.Length);

        return encryptedPrivateKey;
    }

    private static AsymmetricKeyParameter DecryptPrivateKey(
        char[] password, byte[] iv, byte[] salt, byte[] encryptedPrivateKey, byte[] certificate
    )
    {
        // derive key
        Argon2Parameters argonParams = MakeArgonParameters(salt);
        _hash.Init(argonParams);

        byte[] secretKey = new byte[32];
        _hash.GenerateBytes(password, secretKey);

        ChaCha20Poly1305 cryptEngine = new();
        cryptEngine.Init(false, new AeadParameters(new KeyParameter(secretKey), 96, iv));
        byte[] privateKeyEncoded = new byte[encryptedPrivateKey.Length - 16];

        cryptEngine.ProcessBytes(encryptedPrivateKey, 0, encryptedPrivateKey.Length - 16,
            privateKeyEncoded, 0);

        cryptEngine.ProcessAadBytes(certificate);
        cryptEngine.DoFinal(privateKeyEncoded, privateKeyEncoded.Length);

        return PrivateKeyFactory.CreateKey(
            new PrivateKeyInfo(
                new AlgorithmIdentifier(EdECObjectIdentifiers.id_Ed448),
                Asn1Object.FromByteArray(privateKeyEncoded)
            )
        );
    }
}
