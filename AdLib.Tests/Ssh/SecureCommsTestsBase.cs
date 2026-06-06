using AdLib.Identities;

namespace AdLib.Tests.Ssh;

public class SecureCommsTestsBase
{
    protected const string Host = "127.0.0.1";
    protected const string UnknownHost = "example.invalid";

    protected Identity ServerIdentity;
    protected Identity ClientIdentity;
    protected Identity UntrustedServerIdentity;
    protected Identity UntrustedClientIdentity;

    protected readonly TrustStore ServerTrustStore = new();
    protected readonly TrustStore ClientTrustStore = new();

    private static readonly string TempPath = Path.Combine(Path.GetTempPath(), "AdLib");
    private static readonly char[] Password = "".ToCharArray();

    private HostPublicKeyInfo _serverHostPublicKeyInfo;
    private PublicKeyInfo _clientPublicKeyInfo;

    [OneTimeSetUp]
    public void InitializeBaseEnvironment()
    {
        Directory.CreateDirectory(TempPath);

        this.ServerIdentity = Identity.CreateNew(TempPath, "Server", Password);
        this.ClientIdentity = Identity.CreateNew(TempPath, "Client", Password);
        this.UntrustedServerIdentity = Identity.CreateNew(TempPath, "UntrustedServer", Password);
        this.UntrustedClientIdentity = Identity.CreateNew(TempPath, "UntrustedClient", Password);

        this._serverHostPublicKeyInfo = new HostPublicKeyInfo
        {
            Host = Host,
            PublicKeyInfo = new PublicKeyInfo
            {
                PublicKeyFingerprint = PublicKeyInfo.GetCanonicalFingerprint(this.ServerIdentity.Keys),
                FriendlyName = this.ServerIdentity.FriendlyName,
                InternalName = this.ServerIdentity.InternalName,
            },
        };

        this._clientPublicKeyInfo = new PublicKeyInfo
        {
            PublicKeyFingerprint = PublicKeyInfo.GetCanonicalFingerprint(this.ClientIdentity.Keys),
            FriendlyName = this.ClientIdentity.FriendlyName,
            InternalName = this.ClientIdentity.InternalName,
        };

        this.ClientTrustStore.Trust(this._serverHostPublicKeyInfo);
        this.ServerTrustStore.Trust(this._clientPublicKeyInfo);
    }

    [OneTimeTearDown]
    public void CleanUpBaseEnvironment() { Directory.Delete(TempPath, true); }
}