using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdLib.IO;

public class TlsClient
{

    private readonly Dictionary<string, X509Certificate> _trustedCerts = [];
    private readonly Dictionary<string, X509Certificate> _unknownCerts = [];
    private static readonly HashAlgorithmName CertHash = HashAlgorithmName.SHA3_256;

    public delegate void CertificateEventHandler(string hostName, X509Certificate cert);
    public event CertificateEventHandler? OnUntrustedCertificateAdded;
    public event CertificateEventHandler? OnCertificateError;
    public event CertificateEventHandler? OnWrongCertificateHostnameSeen;
    
    private bool ValidateCertificate(
        object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors
    )
    {
        // we can safely ignore a name mismatch - in fact, this is required because the server can change
        // hostnames, and that would make a cert invalid
        sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;

        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            // bad certificate
            return false;
        }

        if (sender is string hostName)
        {
            if (this._trustedCerts.TryGetValue(hostName, out X509Certificate? foundCert))
            {
                // verify that this is the correct certificate
                bool status = foundCert.GetCertHash(CertHash) == certificate.GetCertHash(CertHash);

                if (!status)
                {
                    OnWrongCertificateHostnameSeen?.Invoke(hostName, certificate);
                }
                return status;
            }
            else
            {
                // not in trusted list - ask user for confirmation
                this._unknownCerts.Add(hostName, certificate);
                this.OnUntrustedCertificateAdded?.Invoke(hostName, certificate);

                return false;
            }
        }
        
        // idk
        return false; 
    }
    
}
