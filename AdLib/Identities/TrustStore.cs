using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using AdLib.Cryptography;

using Microsoft.DevTunnels.Ssh.Algorithms;

namespace AdLib.Identities;

public class TrustStore
{
    private readonly Dictionary<string, HostPublicKeyInfo> _trustedHostKeys = [];
    private readonly List<PublicKeyInfo> _trustedKeys = [];

    public IEnumerable<string> KnownHosts => this._trustedHostKeys.Keys;
    public IEnumerable<HostPublicKeyInfo> TrustedHostPublicKeys => this._trustedHostKeys.Values;
    public IEnumerable<PublicKeyInfo> TrustedPublicKeys => this._trustedKeys.AsReadOnly();

    public IEnumerable<IKeyPair> TrustedKeys =>
        this._trustedKeys
            .Select(k => k.PublicKey)
            .Concat(this._trustedHostKeys.Values.Select(c => c.PublicKeyInfo.PublicKey));

    public IEnumerable<PublicKeyInfo> AllTrustedKeys =>
        this._trustedKeys.Concat(this._trustedHostKeys.Values.Select(c => c.PublicKeyInfo));

    public TrustStore() { }

    public TrustStore(IEnumerable<PublicKeyInfo>? trustedKeys)
    {
        if (trustedKeys is not null)
        {
            this.TrustAll(trustedKeys);
        }
    }

    public void Load(string folder, char[]? password)
    {
        // search for all keys
        IEnumerable<string> files = Directory.EnumerateFiles(folder)
                                             .Where(f => f.EndsWith(PublicKeyInfo.FILE_EXTENSION));

        foreach (string file in files)
        {
            // only decrypt if there is a password - otherwise, use plaintext 
            if (password is null)
            {
                // load plaintext
                PublicKeyInfo key = PublicKeyInfo.LoadPublicKeyInfo(file);
                this.Trust(key);
            }
            else
            {
                Lockbox box = Lockbox.DecryptLockbox(File.ReadAllBytes(file), [], password);
                if (box.Data is null) throw new InvalidOperationException("Failed to decrypt lockbox");
                PublicKeyInfo key = PublicKeyInfo.LoadPublicKeyInfo(box.Data);
                this.Trust(key);
            }
        }
    }

    public void Save(string folder, char[]? password)
    {
        IEnumerable<(string, PublicKeyInfo)> files =
            from key in this.AllTrustedKeys
            let path = Path.Combine(folder, GetFileName(key))
            select (path, key);

        foreach ((string path, PublicKeyInfo key) in files)
        {
            byte[] data = key.SerializePublicKeyInfo();

            if (password is not null)
            {
                Lockbox lockbox = Lockbox.Create(data);
                byte[] encrypted = lockbox.EncryptNewLockbox(password, []);
                File.WriteAllBytes(path, encrypted);
            }
            else
            {
                File.WriteAllBytes(path, data);
            }
        }
    }

    private static string GetFileName(PublicKeyInfo key) =>
        key.InternalName.ToString("D") + PublicKeyInfo.FILE_EXTENSION;

    public bool IsPublicKeyValid(HostPublicKeyInfo key) =>
        this._trustedHostKeys.ContainsKey(key.Host) && key.Equals(this._trustedHostKeys[key.Host]);

    public bool IsPublicKeyValid(string? host, IKeyPair? publicKey)
    {
        if (host is null || publicKey is null)
        {
            return false;
        }

        byte[] fingerprint = publicKey.GetPublicKeyBytes().Array;

        return this._trustedHostKeys.ContainsKey(host) &&
               this._trustedHostKeys[host].PublicKeyInfo.PublicKey.GetPublicKeyBytes()
                   .SequenceEqual(fingerprint);
    }

    public bool IsKnown(string host) => this._trustedHostKeys.ContainsKey(host);

    public HostPublicKeyInfo? GetKeyByHostOrDefault(string host, HostPublicKeyInfo? defaultVal = null) =>
        this._trustedHostKeys.GetValueOrDefault(host) ?? defaultVal;

    public PublicKeyInfo? GetKeyByThumbprintOrDefault(
        byte[] thumbprint, HashAlgorithmName keyHash, PublicKeyInfo? defaultVal = null
    ) => this.AllTrustedKeys
             .Where(c => c.PublicKey.GetThumbprint(keyHash).SequenceEqual(thumbprint))
             .FirstOrDefault(defaultVal);

    public void Trust(PublicKeyInfo key) => this._trustedKeys.Add(key);
    public void Trust(HostPublicKeyInfo key) => this._trustedHostKeys[key.Host] = key;

    public void Untrust(string host) => this._trustedHostKeys.Remove(host);
    public void Untrust(PublicKeyInfo key) => this._trustedKeys.Remove(key);

    public void Untrust(HostPublicKeyInfo key)
    {
        if (key.Equals(this._trustedHostKeys[key.Host]))
        {
            this._trustedHostKeys.Remove(key.Host);
        }
    }

    public void TrustAll(IEnumerable<PublicKeyInfo> keys)
    {
        foreach (PublicKeyInfo key in keys)
        {
            this.Trust(key);
        }
    }

    public void TrustAll(IEnumerable<HostPublicKeyInfo> keys)
    {
        foreach (HostPublicKeyInfo key in keys)
        {
            this.Trust(key);
        }
    }

    public TrustStore Combine(TrustStore other)
    {
        TrustStore combined = new();
        combined.TrustAll(this._trustedKeys);
        combined.TrustAll(other._trustedKeys);
        return combined;
    }

    public bool TryGetPublicKey(string host, [MaybeNullWhen(false)] out HostPublicKeyInfo publicKeyInfo) => this._trustedHostKeys.TryGetValue(host, out publicKeyInfo);

    public PublicKeyInfo? FindPublicKeyOrDefault(IKeyPair keys, PublicKeyInfo? defaultValue = null)
    {
        byte[] key = keys.GetPublicKeyBytes().Array;

        return this.AllTrustedKeys.FirstOrDefault(k => k.PublicKey.GetPublicKeyBytes().SequenceEqual(key)) ??
               defaultValue;
    }

    public bool HasPlainKey(IKeyPair? publicKey)
    {
        byte[]? key = publicKey?.GetPublicKeyBytes().Array;
        if (key is null) return false;
        return this._trustedKeys.Any(k => k.PublicKey.GetPublicKeyBytes().SequenceEqual(key));
    }
}