using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using AdLib.Identities;

namespace AdLib.IO;

public sealed class TlsServer : IDisposable
{
    private readonly Identity _identity;
    private readonly TcpListener _listener;

    private readonly TrustStore _trustStore;
    private bool _disposed;

    public TlsServer(Identity identity, TrustStore? trustedCerts = null)
    {
        this._identity = identity;
        this._listener = new TcpListener(IPAddress.Any, TlsUtils.PORT);
        this._trustStore = trustedCerts ?? new TrustStore();
    }
    
    public TlsServer(Identity identity, Certificate[]? trustedCerts = null) : 
        this(identity, new TrustStore(trustedCerts)) { }

    public void Dispose()
    {
        if (this._disposed) return;
        this._listener.Stop();
        this._disposed = true;
    }

    public void Start() => this._listener.Start();

    public void Stop() => this._listener.Stop();

    public TlsUtils.ConnectionInfo AcceptClient()
    {
        TcpClient? tcpClient = this._listener.AcceptTcpClient();
        IPAddress? ip = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;

        NetworkStream networkStream = tcpClient.GetStream();
        X509Certificate? clientCert = null;
        Certificate? realCert = null;
        TlsUtils.ConnectionResult result = TlsUtils.ConnectionResult.Success;
        TlsUtils.RejectionReason reason = TlsUtils.RejectionReason.None;

        SslStream? sslStream = new(networkStream, true, Validate);

        try
        {
            sslStream.AuthenticateAsServer(this._identity.ClrCert, true, SslProtocols.Tls13, true);
        }
        catch (AuthenticationException)
        {
            reason = TlsUtils.CommunicateRejection(tcpClient, result);
            tcpClient.Dispose();
            tcpClient = null;
            sslStream.Dispose();
            sslStream = null;
        }

        return new TlsUtils.ConnectionInfo
        {
            Result = result,
            Reason = reason,
            Hostname = ip?.ToString() ?? "<unknown>",
            InsecureClient = tcpClient,
            SslStream = sslStream,
            Certificate = realCert,
            PresentedCert = clientCert,
        };

        bool Validate(object sender, X509Certificate? cert, X509Chain? _, SslPolicyErrors errors)
        {
            return TlsUtils.ValidateCertificate(sender, cert, errors, this._trustStore, false,
                out result, out realCert, out clientCert);
        }
    }

    public void TrustCertificate(Certificate cert) { this._trustStore.Trust(cert); }
}
