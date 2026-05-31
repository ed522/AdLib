using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;

namespace AdLib.IO;

public sealed class SecureServer : IDisposable
{
    private readonly Identity _identity;
    private readonly TcpListener _listener;

    private readonly TrustStore _trustStore;
    private bool _disposed;

    public SecureServer(Identity identity, TrustStore? trustedCerts = null)
    {
        this._identity = identity;
        this._listener = new TcpListener(IPAddress.Any, SecureConnectionUtils.Port);
        this._trustStore = trustedCerts ?? new TrustStore();
    }

    public SecureServer(Identity identity, Certificate[]? trustedCerts = null) :
        this(identity, new TrustStore(trustedCerts)) { }

    public void Dispose()
    {
        if (this._disposed) return;
        this._listener.Stop();
        this._disposed = true;
    }

    public void Start() => this._listener.Start();

    public void Stop() => this._listener.Stop();

    public async Task<SecureConnectionUtils.ConnectionInfo> AcceptClientAsync(CancellationToken ct = default)
    {
        TcpClient tcpClient = await this._listener.AcceptTcpClientAsync(ct);
        IPAddress? ip = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;

        X509Certificate2? clientCert = null;
        Certificate? realCert = null;
        SecureConnectionUtils.ConnectionResult result = SecureConnectionUtils.ConnectionResult.Success;
        SecureConnectionUtils.RejectionReason reason = SecureConnectionUtils.RejectionReason.None;
        
        SslStream sslStream = new(tcpClient.GetStream(), true, Validate);
        SecureConnection? connection;

        try
        {
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = this._identity.Cert,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            await sslStream.AuthenticateAsServerAsync(options, ct);
            connection = new SecureConnection(tcpClient, sslStream);
        }
        catch (AuthenticationException)
        {
            reason = await SecureConnectionUtils.CommunicateRejectionAsync(tcpClient, result, ct);
            tcpClient.Dispose();
            await sslStream.DisposeAsync();
            connection = null;
        }

        return new SecureConnectionUtils.ConnectionInfo
        {
            Result = result,
            Reason = reason,
            Hostname = ip?.ToString() ?? "<unknown>",
            Connection = connection,
            Certificate = realCert,
            PresentedCert = clientCert,
        };

        bool Validate(object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)
        {
            return SecureConnectionUtils.ValidateCertificate(
                ip?.ToString() ?? "", cert, chain, errors, this._trustStore, false,
                out result, out realCert, out clientCert
            );
        }
    }
}
