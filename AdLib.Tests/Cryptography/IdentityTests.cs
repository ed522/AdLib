using AdLib.Identities;

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;

namespace AdLib.Tests.Cryptography;

[TestFixture]
public class IdentityTests
{
    private const string IdentityName = "testing identity friendly name";
    private const string InvalidIdentityName = "testing identity=friendly name";

    private static readonly char[] Password = "password".ToCharArray();

    private string _storeFolder;

    [SetUp]
    public void Setup()
    {
        this._storeFolder = Path.Combine(Path.GetTempPath(), "AdLibTest");
        Directory.CreateDirectory(this._storeFolder);
    }

    [Test]
    public void ValidClientIdentityCreation_Succeeds()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, true);
        Assert.That(identity, Is.Not.Null);
    }

    [Test]
    public void ClientIdentityAttributesTest()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.PrivateKey, Is.Not.Null);
            Assert.That(identity.Cert, Is.Not.Null);
            Assert.That(identity.ClrCert, Is.Not.Null);
            Assert.That(identity.FriendlyName, Is.Not.Null);
            Assert.That(identity.InternalName, Is.Not.Default);
            Assert.That(identity.Ecdsa, Is.Not.Null);

            Assert.That(identity.FriendlyName, Is.EqualTo(IdentityName));
            Assert.That(identity.Cert.SubjectDN.GetValueList(X509Name.CN)[0], Is.EqualTo(IdentityName));
            Assert.That(identity.ClrCert.HasPrivateKey);
            Assert.That(identity.Cert.GetExtendedKeyUsage().Contains(KeyPurposeID.id_kp_clientAuth));
            Assert.That(!identity.Cert.GetExtendedKeyUsage().Contains(KeyPurposeID.id_kp_serverAuth));
        }
    }

    [Test]
    public void ServerIdentityAttributesTest()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.PrivateKey, Is.Not.Null);
            Assert.That(identity.Cert, Is.Not.Null);
            Assert.That(identity.ClrCert, Is.Not.Null);
            Assert.That(identity.FriendlyName, Is.Not.Null);
            Assert.That(identity.InternalName, Is.Not.Default);
            Assert.That(identity.Ecdsa, Is.Not.Null);

            Assert.That(identity.FriendlyName, Is.EqualTo(IdentityName));
            Assert.That(identity.Cert.SubjectDN.GetValueList(X509Name.CN)[0], Is.EqualTo(IdentityName));
            Assert.That(identity.ClrCert.HasPrivateKey);
            Assert.That(identity.Cert.GetExtendedKeyUsage().Contains(KeyPurposeID.id_kp_serverAuth));
            Assert.That(!identity.Cert.GetExtendedKeyUsage().Contains(KeyPurposeID.id_kp_clientAuth));
        }
    }

    [Test]
    public void IdentityCreationWithEquals_Fails()
    {
        Assert.Throws<ArgumentException>(() =>
            Identity.CreateNew(this._storeFolder, InvalidIdentityName, Password, true)
        );
    }

    [Test]
    public void TestLoadStoreEquality_Equals()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, true);
        Identity loadedIdentity = Identity.LoadFromFile(this._storeFolder, identity.InternalName, Password);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity, Is.Not.Null);
            Assert.That(loadedIdentity, Is.Not.Null);
        }

        Assert.That(identity, Is.EqualTo(loadedIdentity));
    }

    [Test]
    public void TestDataTampering_Throws()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, true,
            out IdentityMetadata meta);

        // addition is a permutation so it will guaranteed be different
        Assume.That(
            () => meta.EncryptedPrivateKey.Length > 17,
            "not enough data to proceed - lockbox impl. issue?"
        );

        // misses tag/salt and stuff, so the tamper test is more representative
        meta.EncryptedPrivateKey[^17] = (byte)(meta.EncryptedPrivateKey[^17] + 127);
        meta.WriteMetadata(this._storeFolder, Identity.GetFileName(identity.InternalName));

        Assert.Throws<InvalidCipherTextException>(() =>
            Identity.LoadFromFile(this._storeFolder, identity.InternalName, Password)
        );
    }

    [TearDown]
    public void TearDown() => Directory.Delete(this._storeFolder, true);
}
