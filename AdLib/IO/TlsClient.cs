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
    private TlsConnection? _connection;

    public SslStream? SslStream => this._connection?.SslStream;
    public bool HasData => this._connection?.HasData ?? false;

    public void Dispose() { this._connection?.Dispose(); }

    public ConnectionInfo Connect(string host)
    {
        TcpClient tcpClient = new();
        tcpClient.Connect(host, Port);

        ConnectionResult result = ConnectionResult.DidNotAttempt;
        X509Certificate2? presentedCert = null;
        Certificate? realCert = null;
        RejectionReason reason = RejectionReason.None;

        SslStream sslStream = new(tcpClient.GetStream(), true, Validate);

        try
        {
            sslStream.AuthenticateAsClient(host, [identity.ClrCert], SslProtocols.Tls13, true);
            // cert + result are now set, unless validator never ran
            this._connection = new TlsConnection(tcpClient, sslStream);
        }
        catch (AuthenticationException)
        {
            reason = CommunicateRejection(tcpClient, result);
            tcpClient.Dispose();

            // clean up (since the streams are invalid now)
            sslStream.Dispose();
            this._connection = null;
        }

        return new ConnectionInfo
        {
            Result = result,
            Hostname = host,
            Connection = this._connection,
            Certificate = realCert,
            PresentedCert = presentedCert,
            Reason = reason,
        };

        // for the `result` capture
        bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            return ValidateCertificate(
                host, certificate, chain, errors, trustedCerts, false,
                out result, out realCert, out presentedCert
            );
        }
    }
}
