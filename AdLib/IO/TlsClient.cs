using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using AdLib.Identities;

namespace AdLib.IO;

public sealed class TlsClient : IDisposable
{

    public const ushort PORT = 7477;

    private readonly Dictionary<string, X509Certificate> _trustedCerts = [];
    private TcpClient? _tcpClient;
    private static readonly HashAlgorithmName CertHash = HashAlgorithmName.SHA3_256;

    public SslStream? SslStream { get; private set; }
    public bool HasData => this._tcpClient?.Available > 0;

    ~TlsClient()
    {
        this.Dispose(false);
    }
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }
    private void Dispose(bool dispose)
    {
        if (dispose)
        {
            this._tcpClient?.Dispose();
            this.SslStream?.Dispose();
        }
    }
    
    public ConnectionResult Connect(string host, Identity identity)
    {
        this._tcpClient = new TcpClient();
        this._tcpClient.Connect(host, PORT);

        ConnectionResult result = ConnectionResult.Success;
        
        this.SslStream = new SslStream(this._tcpClient.GetStream(), false, Validate);

        try
        {
            this.SslStream.AuthenticateAsClient(host, [identity.ClrCert],
                SslProtocols.Tls13, true);
        }
        catch (AuthenticationException)
        {
            // status is captured in result, ignore
            return result;
        }

        return result; // no exception
        
        // for the `result` capture
        bool Validate(object o, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) => 
            this.ValidateCertificate(o, certificate, chain, errors, ref result);
    }

    private const int BUFFER_SIZE = 4096;
    
    private bool ValidateCertificate(
        object sender, X509Certificate? certificate, X509Chain? _, SslPolicyErrors sslPolicyErrors,
        ref ConnectionResult result
    )
    {
        // we can safely ignore a name mismatch - in fact, this is required because the server can change
        // hostnames, and that would make a cert invalid
        sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
        if (certificate is null) return false; // means server didn't even try to authenticate
        
        if (sender is string hostName)
        {
            
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                // bad certificate
                result = ConnectionResult.BadCertificate;
                return false;
            }
            
            if (this._trustedCerts.TryGetValue(hostName, out X509Certificate? foundCert))
            {
                // verify that this is the correct certificate
                bool status = foundCert.GetCertHash(CertHash) == certificate.GetCertHash(CertHash);

                if (!status)
                {
                    result = ConnectionResult.BadCertificate;
                }
                return status;
            }
            else
            {
                // not in trusted list - can ask user for confirmation
                result = ConnectionResult.UntrustedCertificate;
                return false;
            }
        }
        // should be unreachable
        
        // idk
        result = ConnectionResult.BadCertificate;
        throw new InvalidOperationException("Unreachable (sender is not a string)");
    }
    
    public void TrustCertificate(string hostName, X509Certificate certificate)
    {
        this._trustedCerts.Add(hostName, certificate);
    }
    
}

public enum ConnectionResult
{
    Success,
    BadCertificate,
    UntrustedCertificate,
}
