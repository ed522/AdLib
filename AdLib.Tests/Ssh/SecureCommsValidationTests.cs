using System.Security.Claims;

using Microsoft.DevTunnels.Ssh.Events;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.Tests.Ssh;

[TestFixture]
public class SecureCommsValidationTests : SecureCommsTestsBase
{
    // TODO find some way to refactor into TestCaseSource - would need to make TlsTestsBase data static
    [Test]
    public void TestResultOnClientTrustedPublicKey_ReturnsTrue_Success()
    {
        ClaimsPrincipal? value = ClientValidateRemote(
            Host, this.ServerIdentity.Keys, SshAuthenticationType.ServerPublicKey,
            this.ClientTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Not.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnClientIncorrectPublicKey_ReturnsFalse_MismatchedPublicKey()
    {
        ClaimsPrincipal? value = ClientValidateRemote(
            Host, this.UntrustedServerIdentity.Keys, SshAuthenticationType.ServerPublicKey,
            this.ClientTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.MismatchedPublicKey));
        }
    }

    [Test]
    public void TestResultOnClientUnknownHost_ReturnsFalse_UnknownHost()
    {
        ClaimsPrincipal? value = ClientValidateRemote(
            UnknownHost, this.ServerIdentity.Keys, SshAuthenticationType.ServerPublicKey,
            this.ClientTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
        }
    }

    [Test]
    public void TestResultOnClientInvalidMethod_ReturnsFalse_InvalidMethod()
    {
        ClaimsPrincipal? value = ClientValidateRemote(
            Host, this.ServerIdentity.Keys, SshAuthenticationType.ClientPublicKeyQuery,
            this.ClientTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.InvalidMethod));
        }
    }

    [Test]
    public void TestResultOnServerTrustedPublicKey_ReturnsTrue_Success()
    {
        ClaimsPrincipal? value = ServerValidateRemote(
            Host, this.ClientIdentity.Keys, SshAuthenticationType.ClientPublicKey,
            this.ServerTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Not.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.Success));
        }
    }

    [Test]
    public void TestResultOnServerUnknownPublicKey_ReturnsFalse_UnknownHost()
    {
        ClaimsPrincipal? value = ServerValidateRemote(
            Host, this.UntrustedClientIdentity.Keys, SshAuthenticationType.ClientPublicKey,
            this.ServerTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
        }
    }


    [Test]
    public void TestResultOnServerInvalidMethod_ReturnsFalse_InvalidMethod()
    {
        ClaimsPrincipal? value = ClientValidateRemote(
            Host, this.ServerIdentity.Keys, SshAuthenticationType.ClientHostBased,
            this.ClientTrustStore, out ConnectionResult status
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.Null);
            Assert.That(status, Is.EqualTo(ConnectionResult.InvalidMethod));
        }
    }

}