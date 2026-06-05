using System;
using System.Linq;

using AdLib.Cryptography;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;

namespace AdLib.Identities;

public class Identity
{
    private static readonly PublicKeyAlgorithm DefaultKeyAlgorithm = SshAlgorithms.PublicKey.ECDsaSha2Nistp384;

    internal static string GetFileName(Guid internalName) =>
        internalName.ToString("D") + IdentityMetadata.FILE_EXTENSION;

    private Identity(
        IKeyPair keys, string friendlyName, Guid internalName
    )
    {
        this.Keys = keys;
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
        Lockbox box = Lockbox.DecryptLockbox(metadata.EncryptedPrivateKey, metadata.PublicKey, password);

        if (box.Data is null) throw new InvalidOperationException("Secret was empty after decrypting");
        this.Keys = KeyPair.ImportKeyBytes(box.Data);

        this.FriendlyName = metadata.FriendlyName;
    }

    public IKeyPair Keys { get; }
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
        IKeyPair pair = DefaultKeyAlgorithm.GenerateKeyPair();
        byte[] exportedPublicKey = pair.GetPublicKeyBytes().Array;

        byte[] rawPrivateKey = KeyPair.ExportPrivateKeyBytes(pair);
        Lockbox box = Lockbox.Create(rawPrivateKey);
        byte[] encryptedPrivateKey = box.EncryptNewLockbox(password, exportedPublicKey);

        Guid internalName = GetDerivedGuid(exportedPublicKey);
        
        metadata = new IdentityMetadata
        {
            PublicKey = exportedPublicKey,
            EncryptedPrivateKey = encryptedPrivateKey,
            FriendlyName = friendlyName,
            InternalName = internalName,
        };

        metadata.WriteMetadata(storePath, GetFileName(internalName));

        return new Identity(pair, friendlyName, internalName);
    }

    public static Guid GetDerivedGuid(byte[] hash)
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
        this.Keys.GetPublicKeyBytes().SequenceEqual(ident.Keys.GetPublicKeyBytes());

    // leaves out Ecdsa since that has no equality operator
    public override int GetHashCode() =>
        HashCode.Combine(this.InternalName, this.FriendlyName, this.Keys.GetPublicKeyBytes());
}