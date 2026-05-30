using System;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace AdLib.Cryptography;

public class Lockbox
{
    private const int SECRET_LENGTH = 32; // 256 bits
    private const int IV_LENGTH = 12; // 96 bits for ChaCha20
    private const int TAG_LENGTH = 16; // 128 bits for Poly1305
    private static readonly Argon2BytesGenerator _hash = new();

    private byte[]? _iv;
    private byte[]? _salt;

    private Lockbox(byte[]? data, byte[]? salt, byte[]? iv)
    {
        this.Data = data;
        this._salt = salt;
        this._iv = iv;
    }

    public byte[]? Data { get; set; }

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

    private static void Increment(byte[]? iv)
    {
        if (iv is null)
        {
            throw new InvalidOperationException("Lockbox has not been initialized");
        }

        for (int i = iv.Length - 1; i >= 0; i--)
        {
            if (iv[i] == 0xFF)
            {
                iv[i] = 0;
            }
            else
            {
                iv[i]++;
                return;
            }
        }

        throw new ExhaustedKeyException("Cannot increment IV any further. A new lockbox must be created.");
    }

    private byte[] EncryptDirectly(char[] password, byte[] aad)
    {
        if (this.Data is null || this._salt is null || this._iv is null)
        {
            throw new InvalidOperationException("Lockbox has not been initialized");
        }

        // derive secret
        Argon2Parameters argonParams = MakeArgonParameters(this._salt);
        _hash.Init(argonParams);
        byte[] secretKey = new byte[SECRET_LENGTH];
        _hash.GenerateBytes(password, secretKey);

        // ChaCha20 is a solid stream cipher but the nonce is 96 bits, so it can't be randomized
        ChaCha20Poly1305 cryptEngine = new();
        cryptEngine.Init(true, new AeadParameters(new KeyParameter(secretKey), TAG_LENGTH * 8, this._iv));

        byte[] encryptedData = new byte[cryptEngine.GetOutputSize(this.Data.Length)]; // 128 bit tag

        // encrypt data with AAD
        cryptEngine.ProcessAadBytes(aad);
        int processed = cryptEngine.ProcessBytes(this.Data, 0, this.Data.Length, encryptedData, 0);
        // adds tag after data
        cryptEngine.DoFinal(encryptedData, processed);

        byte[] total = new byte[encryptedData.Length + SECRET_LENGTH + IV_LENGTH];
        this._salt.CopyTo(total, 0);
        this._iv.CopyTo(total, SECRET_LENGTH);
        encryptedData.CopyTo(total, SECRET_LENGTH + IV_LENGTH);

        return total;
    }

    public byte[] EncryptNewLockbox(char[] password, byte[] aad)
    {
        if (this.Data is null)
        {
            throw new InvalidOperationException("Lockbox does not have any data");
        }

        // initialize lockbox
        // create a competely new salt - since this is a new lockbox, we use a new key
        SecureRandom random = new();
        this._salt = new byte[SECRET_LENGTH]; // matches key length
        random.NextBytes(this._salt);

        // start each IV at all 0
        byte[] iv = new byte[IV_LENGTH];
        this._iv = iv;

        return this.EncryptDirectly(password, aad);
    }

    public byte[] ReencryptLockbox(char[] password, byte[] aad)
    {
        if (this.Data is null)
        {
            throw new InvalidOperationException("Lockbox does not have any data");
        }

        if (this._salt is null || this._iv is null)
        {
            throw new InvalidOperationException("Lockbox has not been initialized - use EncryptNewLockbox " +
                                                "instead");
        }
        
        // increment IV
        Increment(this._iv);

        // encrypt using already-setup data
        return this.EncryptDirectly(password, aad);
    }

    public static Lockbox DecryptLockbox(byte[] data, byte[] aad, char[] password)
    {
        byte[] salt = new byte[SECRET_LENGTH];
        byte[] iv = new byte[IV_LENGTH];
        byte[] encryptedData = new byte[data.Length - SECRET_LENGTH - IV_LENGTH];

        Array.Copy(data, 0, salt, 0, SECRET_LENGTH);
        Array.Copy(data, SECRET_LENGTH, iv, 0, IV_LENGTH);
        Array.Copy(data, SECRET_LENGTH + IV_LENGTH, encryptedData, 0, encryptedData.Length);

        // derive key
        Argon2Parameters argonParams = MakeArgonParameters(salt);
        _hash.Init(argonParams);

        byte[] secretKey = new byte[SECRET_LENGTH];
        _hash.GenerateBytes(password, secretKey);

        ChaCha20Poly1305 cryptEngine = new();
        cryptEngine.Init(false, new AeadParameters(new KeyParameter(secretKey), TAG_LENGTH * 8, iv));
        byte[] plaintext = new byte[cryptEngine.GetOutputSize(encryptedData.Length)];

        cryptEngine.ProcessAadBytes(aad);
        int processed = cryptEngine.ProcessBytes(encryptedData, 0, encryptedData.Length, plaintext, 0);
        cryptEngine.DoFinal(plaintext, processed);

        return new Lockbox(plaintext, salt, iv);
    }

    public static Lockbox Create() => new(null, null, null);
    public static Lockbox Create(byte[] data) => new(data, null, null);
}
