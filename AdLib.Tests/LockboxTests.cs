using AdLib.Cryptography;

using Org.BouncyCastle.Crypto;

namespace AdLib.Tests;

public class LockboxTests
{
    private static char[] Password => "abcdabcd".ToCharArray();
    private static char[] WrongPassword => "WRONG PASSWORD".ToCharArray();
    private static byte[] TestCaseData => [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
    private static byte[] TestCaseFilledAad => [0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA];
    private static byte[] TestCaseEmptyAad => [];

    #region Increment(byte[]) tests

    [Test]
    public void TestLockboxIncrementNull_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Lockbox.Increment(null));

    [Test]
    public void TestLockboxIncrementBaseCase()
    {
        byte[] data = [0xF9, 0x68, 0x29, 0x0C];
        byte[] expected = [0xF9, 0x68, 0x29, 0x0D];
        Lockbox.Increment(data);
        Assert.That(data, Is.EqualTo(expected));
    }

    [Test]
    public void TestLockboxIncrementCarry()
    {
        byte[] data = [0x90, 0xFF, 0xFF, 0xFF];
        byte[] expected = [0x91, 0x00, 0x00, 0x00];
        Lockbox.Increment(data);
        Assert.That(data, Is.EqualTo(expected));
    }

    [Test]
    public void TestLockboxIncrementOverflow_Throws()
    {
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF];
        Assert.Throws<ExhaustedKeyException>(() => Lockbox.Increment(data));
    }

    #endregion

    #region Round-trip tests (should succeed)

    [Test]
    public void TestLockboxRoundTripBasic()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);
        Lockbox testCase = Lockbox.DecryptLockbox(encrypted, aad, Password);

        Assert.That(basis.Data, Is.EqualTo(data));
        Assert.That(testCase.Data, Is.EqualTo(basis.Data));
    }

    [Test]
    public void TestLockboxRoundTripAad()
    {
        byte[] data = TestCaseData;
        byte[] expected = TestCaseData;
        byte[] aad = TestCaseFilledAad;
        Assert.That(data, Is.EqualTo(expected), "incorrectly set up test data");

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);
        Lockbox testCase = Lockbox.DecryptLockbox(encrypted, aad, Password);

        Assert.That(basis.Data, Is.EqualTo(data));
        Assert.That(testCase.Data, Is.EqualTo(basis.Data));
    }

    #endregion

    #region Tampering with the data (should fail)

    [Test]
    public void TestLockboxTamperData_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);

        Assert.That(encrypted, Has.Length.GreaterThan(18), "not enough encrypted data");

        // miss the tag
        encrypted[^18] = unchecked((byte)(encrypted[0] + 0x7F));

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, Password);
        });
    }

    [Test]
    public void TestLockboxTamperTag_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);
        Assert.That(encrypted, Has.Length.GreaterThan(2), "not enough encrypted data");

        encrypted[^2] = unchecked((byte)(encrypted[0] + 0x7F));

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, Password);
        });
    }

    [Test]
    public void TestLockboxTamperAad_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseFilledAad;
        Assert.That(aad[0], Is.Not.EqualTo(0x00), "incorrectly set up test data");

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);
        aad[0] = unchecked((byte)~aad[0]);

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, Password);
        });
    }

    #endregion

    #region Corrupt or incorrect data (should fail)

    [Test]
    public void TestLockboxCorruptSalt_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);

        encrypted[0] = unchecked((byte)(encrypted[0] + 0x7F));

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, Password);
        });
    }

    [Test]
    public void TestLockboxCorruptIv_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);

        Assert.That(encrypted, Has.Length.GreaterThan(33), "not enough encrypted data (where's the salt?)");
        encrypted[33] = unchecked((byte)(encrypted[33] + 0x7F));

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, Password);
        });
    }

    [Test]
    public void TestLockboxWrongPassword_Throws()
    {
        byte[] data = TestCaseData;
        byte[] aad = TestCaseEmptyAad;

        Lockbox basis = Lockbox.Create(data);
        byte[] encrypted = basis.EncryptNewLockbox(Password, aad);

        Assert.Throws<InvalidCipherTextException>(() =>
        {
            Lockbox.DecryptLockbox(encrypted, aad, WrongPassword);
        });
    }

    #endregion

    #region Null tests

    [Test]
    public void TestLockboxNullData_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
        {
            Lockbox.Create().EncryptNewLockbox(Password, TestCaseEmptyAad);
        });

    [Test]
    public void TestLockboxNullParameters_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
        {
            // both null iv and salt
            Lockbox.Create(TestCaseData).ReencryptLockbox(Password, TestCaseEmptyAad);
        });

    #endregion
}
