using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdLib.IO;

public static class TlsUtils
{
    public delegate void AuthenticationErrorHandler(
        string host, X509Certificate cert, ConnectionResult result, RejectionReason reason
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
        object sender, X509Certificate? certificate, SslPolicyErrors sslPolicyErrors,
        Dictionary<string, X509Certificate> trustedCerts,
        out ConnectionResult result, out X509Certificate? certOut
    )
    {
        // just so we can get a hold of the cert in user code
        certOut = certificate;
        // we can safely ignore a name mismatch - in fact, this is required because the server can change
        // hostnames, and that would make a cert invalid
        sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;

        if (certificate is null) // means server didn't even try to authenticate
        {
            result = ConnectionResult.BadCertificate;
            return false;
        }

        if (sender is string hostName)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                // bad certificate
                result = ConnectionResult.BadCertificate;
                return false;
            }

            if (trustedCerts.TryGetValue(hostName, out X509Certificate? foundCert))
            {
                // verify that this is the correct certificate
                bool status = foundCert.GetCertHash(CertHash) == certificate.GetCertHash(CertHash);

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

            // not in trusted list - can ask user for confirmation
            result = ConnectionResult.UntrustedCertificate;
            return false;
        }
        // should be unreachable

        // idk
        result = ConnectionResult.UnspecifiedError;
        throw new InvalidOperationException("Unreachable (sender is not a string)");
    }

    /// <summary>
    ///     Communicates with the specified remote host to get rejection reasons over an unsecured channel.
    ///     The
    ///     local host will both tell the remote host why it rejected the connection, and return a reason
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
        public TcpClient? InsecureClient;
        public SslStream? SslStream;
        public X509Certificate? Certificate;
    }
}
