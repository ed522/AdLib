using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using AdLib.Identities;

namespace AdLib.Tests.Tls;

public class TlsTestsBase
{
    #region Shared test data

    protected const string Host = "localhost";
    protected const string UnknownHost = "example.com";

    protected static readonly HashAlgorithmName CertHash = HashAlgorithmName.SHA3_256;

    protected Identity ServerIdentity;
    protected Identity ClientIdentity;
    protected Identity UntrustedServerIdentity;
    protected Identity UntrustedClientIdentity;

    protected readonly X509Chain ServerChain = X509Chain.Create();
    protected readonly X509Chain ClientChain = X509Chain.Create();
    protected readonly X509Chain UntrustedServerChain = X509Chain.Create();
    protected readonly X509Chain UntrustedClientChain = X509Chain.Create();

    protected readonly TrustStore ServerTrustStore = new();
    protected readonly TrustStore ClientTrustStore = new();

    #endregion

    private static readonly string TempPath = Path.Combine(Path.GetTempPath(), "AdLib");
    private static readonly char[] Password = "".ToCharArray();

    private HostCertificate _serverHostCertificate;
    private Certificate _clientCertificate;

    [OneTimeSetUp]
    public void InitializeBaseEnvironment()
    {
        Directory.CreateDirectory(TempPath);

        this.ServerIdentity = Identity.CreateNew(TempPath, "Server", Password);
        this.ClientIdentity = Identity.CreateNew(TempPath, "Client", Password);
        this.UntrustedServerIdentity = Identity.CreateNew(TempPath, "UntrustedServer", Password);
        this.UntrustedClientIdentity = Identity.CreateNew(TempPath, "UntrustedClient", Password);

        X509ChainPolicy clientPolicy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority |
                                X509VerificationFlags.IgnoreWrongUsage,
        };

        clientPolicy.CustomTrustStore.Add(this.ClientIdentity.Cert);

        X509ChainPolicy serverPolicy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority |
                                X509VerificationFlags.IgnoreWrongUsage,
        };

        serverPolicy.CustomTrustStore.Add(this.ServerIdentity.Cert);

        X509ChainPolicy untrustedClientPolicy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority |
                                X509VerificationFlags.IgnoreWrongUsage,
        };

        untrustedClientPolicy.CustomTrustStore.Add(this.UntrustedClientIdentity.Cert);

        X509ChainPolicy untrustedServerPolicy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority |
                                X509VerificationFlags.IgnoreWrongUsage,
        };

        untrustedServerPolicy.CustomTrustStore.Add(this.UntrustedServerIdentity.Cert);

        this.ServerChain.ChainPolicy = serverPolicy;
        this.ClientChain.ChainPolicy = clientPolicy;
        this.UntrustedServerChain.ChainPolicy = untrustedServerPolicy;
        this.UntrustedClientChain.ChainPolicy = untrustedClientPolicy;
        this.ServerChain.Build(this.ServerIdentity.Cert);
        this.ClientChain.Build(this.ClientIdentity.Cert);
        this.UntrustedServerChain.Build(this.UntrustedServerIdentity.Cert);
        this.UntrustedClientChain.Build(this.UntrustedClientIdentity.Cert);

        this._serverHostCertificate = new HostCertificate
        {
            Host = Host,
            Certificate = new Certificate
            {
                X509Cert = this.ServerIdentity.Cert,
                FriendlyName = this.ServerIdentity.FriendlyName,
                InternalName = this.ServerIdentity.InternalName,
            },
        };

        this._clientCertificate = new Certificate
        {
            X509Cert = this.ClientIdentity.Cert,
            FriendlyName = this.ClientIdentity.FriendlyName,
            InternalName = this.ClientIdentity.InternalName,
        };

        this.ClientTrustStore.Trust(this._serverHostCertificate);
        this.ServerTrustStore.Trust(this._clientCertificate);
    }

    [OneTimeTearDown]
    public void CleanUpBaseEnvironment()
    {
        Directory.Delete(TempPath, true);
        this.ServerChain.Dispose();
        this.ClientChain.Dispose();
        this.UntrustedServerChain.Dispose();
        this.UntrustedClientChain.Dispose();
    }
}