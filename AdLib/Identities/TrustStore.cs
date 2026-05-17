using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

using AdLib.Cryptography;

namespace AdLib.Identities;

public class TrustStore
{
    private readonly Dictionary<string, HostCertificate> _trustedHostCerts = [];
    private readonly List<Certificate> _trustedCerts = [];

    public IEnumerable<string> KnownHosts => this._trustedHostCerts.Keys;
    public IEnumerable<HostCertificate> TrustedHostCertificates => this._trustedHostCerts.Values;
    public IEnumerable<Certificate> TrustedCertificates => this._trustedCerts.AsReadOnly();

    public IEnumerable<X509Certificate> TrustedX509Certificates =>
        this._trustedCerts.Select(cert => cert.X509Cert)
                          .Concat(this._trustedHostCerts.Values.Select(c => c.Certificate.X509Cert));
    
    public IEnumerable<Certificate> AllTrustedCertificates =>
        this._trustedCerts.Concat(this._trustedHostCerts.Values.Select(c => c.Certificate));

    public TrustStore() { }
    public TrustStore(IEnumerable<Certificate>? trustedCerts)
    {
        if (trustedCerts is not null)
        {
            this.TrustAll(trustedCerts);
        }
    }
    
    public void Load(string folder, char[]? password)
    {
        // search for all keys
        IEnumerable<string> files = Directory.EnumerateFiles(folder)
                                             .Where(f => f.EndsWith(Certificate.FILE_EXTENSION));

        foreach (string file in files)
        {
            // only decrypt if there is a password - otherwise, use plaintext 
            if (password is null)
            {
                // load plaintext
                Certificate cert = Certificate.LoadCertificate(file);
                this.Trust(cert);
            }
        }
    }

    public void Save(string folder, char[]? password)
    {
        IEnumerable<(string, Certificate)> files =
            from cert in this.AllTrustedCertificates
            let path = Path.Combine(folder, cert.InternalName + Certificate.FILE_EXTENSION)
            select (path, cert);

        foreach ((string path, Certificate cert) in files)
        {
            // string path = Path.Combine(folder, cert.InternalName + Certificate.FILE_EXTENSION);
            byte[] data = cert.SerializeCertificate();

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

    public bool IsCertificateValid(HostCertificate cert) =>
        this._trustedHostCerts.ContainsKey(cert.Host) && cert.Equals(this._trustedHostCerts[cert.Host]);

    public bool IsKnown(string host) => this._trustedHostCerts.ContainsKey(host);
    public HostCertificate? GetCertificate(string host) => this._trustedHostCerts.GetValueOrDefault(host);

    public void Trust(Certificate cert) => this._trustedCerts.Add(cert);
    public void Trust(HostCertificate cert) => this._trustedHostCerts[cert.Host] = cert;

    public void Untrust(string host) => this._trustedHostCerts.Remove(host);
    public void Untrust(Certificate cert) => this._trustedCerts.Remove(cert);

    public void Untrust(HostCertificate cert)
    {
        if (cert.Equals(this._trustedHostCerts[cert.Host]))
            this._trustedHostCerts.Remove(cert.Host);
    }

    public void TrustAll(IEnumerable<Certificate> certs)
    {
        foreach (Certificate cert in certs)
        {
            this.Trust(cert);
        }
    }

    public void TrustAll(IEnumerable<HostCertificate> certs)
    {
        foreach (HostCertificate cert in certs)
        {
            this.Trust(cert);
        }
    }

    public TrustStore Combine(TrustStore other)
    {
        TrustStore combined = new();
        combined.TrustAll(this._trustedCerts);
        combined.TrustAll(other._trustedCerts);
        return combined;
    }

    public bool TryGetCertificate(string host, [MaybeNullWhen(false)] out HostCertificate certificate)
    {
        return this._trustedHostCerts.TryGetValue(host, out certificate);
    }
}
