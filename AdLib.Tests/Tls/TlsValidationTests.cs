using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using AdLib.IO;

namespace AdLib.Tests.Tls;

[TestFixture]
public class TlsValidationTests : TlsTestsBase
{
    #region Test cert output

    // TODO find some way to refactor into TestCaseSource - would need to make TlsTestsBase data static
    [Test]
    public void TestValidationLogicOnTrustedCertificate_OutputsRemoteCertificate()
    {
        SecureConnectionUtils.ValidateCertificate(
            Host, this.ServerIdentity.Cert, this.ServerChain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out _, out _, out X509Certificate2? presentedCert
        );

        Assert.That(presentedCert, Is.Not.Null);

        Assert.That(
            presentedCert.GetCertHash(CertHash),
            Is.EqualTo(this.ServerIdentity.Cert.GetCertHash(CertHash))
        );
    }

    [Test]
    public void TestClientValidationLogicOnUntrustedCertificate_OutputsRemoteCertificate()
    {
        SecureConnectionUtils.ValidateCertificate(
            Host, this.UntrustedServerIdentity.Cert, this.UntrustedServerChain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out _, out _, out X509Certificate2? presentedCert
        );

        Assert.That(presentedCert, Is.Not.Null);

        Assert.That(
            presentedCert.GetCertHash(CertHash),
            Is.EqualTo(this.UntrustedServerIdentity.Cert.GetCertHash(CertHash))
        );
    }

    [Test]
    public void TestServerValidationLogicOnTrustedCertificate_OutputsRemoteCertificate()
    {
        SecureConnectionUtils.ValidateCertificate(
            Host, this.ClientIdentity.Cert, this.ClientChain, SslPolicyErrors.None,
            this.ServerTrustStore, false,
            out _, out _, out X509Certificate2? presentedCert
        );

        Assert.That(presentedCert, Is.Not.Null);

        Assert.That(
            presentedCert.GetCertHash(CertHash),
            Is.EqualTo(this.ClientIdentity.Cert.GetCertHash(CertHash))
        );
    }

    [Test]
    public void TestServerValidationLogicOnUntrustedCertificate_OutputsRemoteCertificate()
    {
        SecureConnectionUtils.ValidateCertificate(
            Host, this.UntrustedClientIdentity.Cert, this.UntrustedClientChain, SslPolicyErrors.None,
            this.ServerTrustStore, false,
            out _, out _, out X509Certificate2? presentedCert
        );

        Assert.That(presentedCert, Is.Not.Null);

        Assert.That(
            presentedCert.GetCertHash(CertHash),
            Is.EqualTo(this.UntrustedClientIdentity.Cert.GetCertHash(CertHash))
        );
    }

    #endregion

    #region Test return value & status

    private static X509Certificate2 CorruptSignature(X509Certificate2 cert)
    {
        byte[] exported = cert.Export(X509ContentType.Cert);
        exported[^5] = (byte)~exported[^5];
        return X509CertificateLoader.LoadCertificate(exported);
    }

    [Test]
    public void TestResultOnClientTrustedCertificate_ReturnsTrue_Success()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.ServerIdentity.Cert, this.ServerChain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.True);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnClientTrustedCertificateMismatchedName_ReturnsTrue_Success()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.ServerIdentity.Cert, this.ServerChain,
            SslPolicyErrors.RemoteCertificateNameMismatch, this.ClientTrustStore, true,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.True);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnClientIncorrectCertificate_ReturnsFalse_MismatchedCertificate()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.UntrustedServerIdentity.Cert, this.UntrustedServerChain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.False);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.MismatchedCertificate));
        }
    }

    [Test]
    public void TestResultOnClientUnknownHost_ReturnsFalse_UntrustedCertificate()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            UnknownHost, this.ServerIdentity.Cert, this.ServerChain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.False);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.UntrustedCertificate));
        }
    }

    [Test]
    public void TestResultOnClientBadSignature_ReturnsFalse_BadCertificate()
    {
        X509Certificate2 cert = CorruptSignature(this.ServerIdentity.Cert);
        X509Chain chain = new();
        chain.Build(cert);

        bool ret = SecureConnectionUtils.ValidateCertificate(
            UnknownHost, cert, chain, SslPolicyErrors.None,
            this.ClientTrustStore, true,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.False);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.BadCertificate));
        }
    }


    [Test]
    public void TestResultOnServerTrustedCertificate_ReturnsTrue_Success()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.ClientIdentity.Cert, this.ClientChain, SslPolicyErrors.None,
            this.ServerTrustStore, false,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.True);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnServerTrustedCertificateMismatchedName_ReturnsTrue_Success()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.ClientIdentity.Cert, this.ClientChain,
            SslPolicyErrors.RemoteCertificateNameMismatch, this.ServerTrustStore, false,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.True);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnServerUnknownCertificate_ReturnsFalse_UntrustedCertificate()
    {
        bool ret = SecureConnectionUtils.ValidateCertificate(
            Host, this.UntrustedClientIdentity.Cert, this.UntrustedClientChain, SslPolicyErrors.None,
            this.ServerTrustStore, false,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.False);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.UntrustedCertificate));
        }
    }

    [Test]
    public void TestResultOnServerBadSignature_ReturnsFalse_BadCertificate()
    {
        X509Certificate2 cert = CorruptSignature(this.ClientIdentity.Cert);
        X509Chain chain = new();
        chain.Build(cert);

        bool ret = SecureConnectionUtils.ValidateCertificate(
            UnknownHost, cert, chain, SslPolicyErrors.None,
            this.ServerTrustStore, false,
            out SecureConnectionUtils.ConnectionResult status, out _, out _
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ret, Is.False);
            Assert.That(status, Is.EqualTo(SecureConnectionUtils.ConnectionResult.BadCertificate));
        }
    }

    #endregion
}