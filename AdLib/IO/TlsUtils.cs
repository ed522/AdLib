using System;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using AdLib.Identities;

namespace AdLib.IO;

public static class TlsUtils
{
    public delegate void AuthenticationErrorHandler(
        string host, Certificate? cert, X509Certificate? presentedCert,
        ConnectionResult result, RejectionReason reason
    );

    public enum ConnectionResult : byte
    {
        DidNotAttempt = 0,
        Success,
        BadCertificate,
        UntrustedCertificate,
        MismatchedCertificate,
        UnspecifiedError,
    }

    public enum RejectionReason : byte
    {
        None = 0x00,
        UntrustedCertificate = 0x01,
        MismatchedCertificate = 0x02,
        BadCertificate = 0x03,
        UnspecifiedError = 0xFF,
    }

    public const ushort PORT = 7477;
    public const short RECOVERY_PORT = 7478;

    private static readonly HashAlgorithmName CertHash = HashAlgorithmName.SHA3_256;

    public static bool ValidateCertificate(
        object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors,
        TrustStore trustedCerts, bool validateHostnames,
        out ConnectionResult result, out Certificate? certInfo, out X509Certificate2? presentedCert
    )
    {
        // NOTE: do not use `certificate` directly since it lacks x509certificate2 props/methods
        // get a hold of the cert in user code 
        presentedCert = certificate is null ? null : new X509Certificate2(certificate);

        // not validated yet, so we can't return anything'
        certInfo = null;

        if (presentedCert is null) // means server didn't even try to authenticate
        {
            result = ConnectionResult.BadCertificate;
            return false;
        }

        // we can safely ignore a name mismatch - in fact, this is required because the server can change
        // hostnames, and that would make a cert invalid
        sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
        sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;

        // ignore:
        // - circular chains (self-signed)
        // - group policy weirdness
        // - self-signed roots
        // - partial chains (self-signing again)
        // - bad/no revocation server (bc they can't be revoked)
        const X509ChainStatusFlags ignoredFlags =
            X509ChainStatusFlags.Cyclic | X509ChainStatusFlags.NoIssuanceChainPolicy |
            X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain |
            X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

        // reassess chain to see if there'll be any non-ignored errors
        if (chain?.ChainStatus.Any(s => (s.Status & ~ignoredFlags) != X509ChainStatusFlags.NoError) ?? false)
        {
            sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors;
        }

        if (sender is string hostName)
        {
            // some error
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                // bad certificate
                result = ConnectionResult.BadCertificate;
                return false;
            }

            Certificate? cert;

            if (validateHostnames)
            {
                if (trustedCerts.TryGetCertificate(hostName, out HostCertificate? foundCert))
                {
                    cert = foundCert.Certificate;
                }
                else
                {
                    // untrusted, but not spoofed - can ask user for confirmation
                    result = ConnectionResult.UntrustedCertificate;
                    return false;
                }
            }
            else
            {
                // validate by thumbprint
                byte[] presentedCertHash = presentedCert.GetCertHash(CertHash);

                Certificate? possibleCert = 
                    trustedCerts.GetCertificateByThumbprintOrDefault(presentedCertHash, CertHash);

                if (possibleCert is null) // no match
                {
                    // ^^
                    result = ConnectionResult.UntrustedCertificate;
                    return false;
                }

                cert = possibleCert;
            }

            X509Certificate2 realCert = cert.X509Cert;
            bool status = realCert.GetCertHash(CertHash).SequenceEqual(presentedCert.GetCertHash(CertHash));

            if (status)
            {
                // certs match, authentication successful
                result = ConnectionResult.Success;
                return true;
            }

            // we've seen this host before, but the cert is different. someone might be trying to
            // spoof us - do not trust, do not ask for confirmation
            result = ConnectionResult.MismatchedCertificate;
            return false;
        }
        // should be unreachable

        // idk
        result = ConnectionResult.UnspecifiedError;
        return false;
    }

    /// <summary>
    ///     Communicates with the specified remote host to get rejection reasons over an unsecured channel.
    ///     The local host will both tell the remote host why it rejected the connection, and return a reason
    ///     provided by the remote host (both if applicable).
    /// </summary>
    /// <param name="client">the client to communicate over</param>
    /// <param name="result">the result of authentication on this host</param>
    /// <returns></returns>
    public static RejectionReason CommunicateRejection(TcpClient client, ConnectionResult result)
    {
        client.GetStream().WriteByte((byte)(result switch
        {
            ConnectionResult.BadCertificate => RejectionReason.BadCertificate,
            ConnectionResult.MismatchedCertificate => RejectionReason.MismatchedCertificate,
            ConnectionResult.UntrustedCertificate => RejectionReason.UntrustedCertificate,
            ConnectionResult.UnspecifiedError => RejectionReason.UnspecifiedError,
            ConnectionResult.Success => RejectionReason.None,
            _ => RejectionReason.UnspecifiedError,
        }));

        // see if there's a reason (defaults to None)
        int reasonCode = client.GetStream().ReadByte();

        // make sure stream isn't closed yet
        if (reasonCode != -1)
        {
            return (RejectionReason)reasonCode;
        }

        return RejectionReason.None;
    }

    public struct ConnectionInfo
    {
        public ConnectionResult Result;
        public RejectionReason Reason;
        public string Hostname;
        public X509Certificate2? PresentedCert;
        public TcpClient? InsecureClient;
        public SslStream? SslStream;
        public Certificate? Certificate;
    }
}
