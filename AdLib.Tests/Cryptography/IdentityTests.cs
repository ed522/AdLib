using AdLib.Identities;

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
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password);
        Assert.That(identity, Is.Not.Null);
    }

    [Test]
    public void ClientIdentityAttributesTest()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Cert, Is.Not.Null);
            Assert.That(identity.FriendlyName, Is.Not.Null);
            Assert.That(identity.InternalName, Is.Not.Default);
            Assert.That(identity.Keys, Is.Not.Null);

            Assert.That(identity.FriendlyName, Is.EqualTo(IdentityName));

            Assert.That(
                identity.Cert.SubjectName.Name
                        .Split(",")
                        .Where(s => s.StartsWith("CN="))
                        .Select(s => s.Replace("CN=", ""))
                        .First(),
                Is.EqualTo(IdentityName)
            );

            Assert.That(identity.Cert.HasPrivateKey);
        }
    }

    [Test]
    public void ServerIdentityAttributesTest()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Cert, Is.Not.Null);
            Assert.That(identity.FriendlyName, Is.Not.Null);
            Assert.That(identity.InternalName, Is.Not.Default);
            Assert.That(identity.Keys, Is.Not.Null);

            Assert.That(identity.FriendlyName, Is.EqualTo(IdentityName));

            Assert.That(
                identity.Cert.SubjectName.Name
                        .Split(",")
                        .Where(s => s.StartsWith("CN="))
                        .Select(s => s.Replace("CN=", ""))
                        .First(),
                Is.EqualTo(IdentityName)
            );

            Assert.That(identity.Cert.HasPrivateKey);
        }
    }

    [Test]
    public void IdentityCreationWithEquals_Fails()
    {
        Assert.Throws<ArgumentException>(() =>
            Identity.CreateNew(this._storeFolder, InvalidIdentityName, Password)
        );
    }

    [Test]
    public void TestLoadStoreEquality_Equals()
    {
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password);
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
        Identity identity = Identity.CreateNew(this._storeFolder, IdentityName, Password, out IdentityMetadata meta);

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