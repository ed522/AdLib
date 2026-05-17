using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using AdLib.Identities;

using static AdLib.IO.TlsUtils;

namespace AdLib.IO;

public sealed class TlsClient(Identity identity, TrustStore trustedCerts) : IDisposable
{
    private TcpClient? _tcpClient;

    public SslStream? SslStream { get; private set; }
    public bool HasData => this._tcpClient?.Available > 0;

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TlsClient() { this.Dispose(false); }

    private void Dispose(bool dispose)
    {
        if (dispose)
        {
            this._tcpClient?.Dispose();
            this.SslStream?.Dispose();
        }
    }

    public ConnectionInfo Connect(string host)
    {
        this._tcpClient = new TcpClient();
        this._tcpClient.Connect(host, PORT);

        ConnectionResult result = ConnectionResult.DidNotAttempt;
        X509Certificate? cert = null;
        RejectionReason reason = RejectionReason.None;

        this.SslStream = new SslStream(this._tcpClient.GetStream(), true, Validate);

        try
        {
            this.SslStream.AuthenticateAsClient(host, [identity.ClrCert], SslProtocols.Tls13, true);
            // cert + result are now set, unless validator never ran
        }
        catch (AuthenticationException)
        {
            reason = CommunicateRejection(this._tcpClient, result);
            this._tcpClient.Dispose();

            // clean up (since the streams are invalid now)
            this.SslStream.Dispose();
            this.SslStream = null;
        }

        return new ConnectionInfo
        {
            Result = result,
            Hostname = host,
            SslStream = this.SslStream,
            InsecureClient = this._tcpClient,
            Certificate = cert,
            Reason = reason,
        };

        // for the `result` capture
        bool Validate(object sender, X509Certificate? certificate, X509Chain? _, SslPolicyErrors errors)
        {
            return ValidateCertificate(sender, certificate, errors, this._trustedCerts, out result, out cert);
        }
    }

    public void TrustCertificate(HostCertificate cert) => trustedCerts.Trust(cert);
}
