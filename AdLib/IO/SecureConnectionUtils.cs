using System.IO;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;

using Microsoft.DevTunnels.Ssh.Algorithms;
using Microsoft.DevTunnels.Ssh.Events;

namespace AdLib.IO;

public static class SecureConnectionUtils
{
    public delegate void AuthenticationErrorHandler(
        string host, PublicKeyInfo? foundKey, IKeyPair? presentedKey,
        ConnectionResult result, RejectionReason reason
    );

    public enum ConnectionResult : byte
    {
        DidNotAttempt = 0,
        Success = 0x01,
        BadPublicKey = 0x10,
        UnknownHostOrKey = 0x11,
        MismatchedPublicKey = 0x12,
        InvalidMethod = 0x20,
        UnspecifiedError = 0xFF,
    }

    public enum RejectionReason : byte
    {
        None = 0x00,
        UnknownHostOrKey = 0x10,
        MismatchedPublicKey = 0x11,
        BadPublicKey = 0x12,
        InvalidMethod = 0x20,
        CouldNotGetReason = 0xFE,
        UnspecifiedError = 0xFF,
    }

    public const ushort Port = 7477;
    public const ushort RecoveryPort = 7478;

    public static ClaimsPrincipal? ServerValidateRemote(
        string? host, IKeyPair? publicKey, SshAuthenticationType type, TrustStore trustedCerts,
        out ConnectionResult result
    )
    {
        result = ConnectionResult.UnspecifiedError;

        // assert proper method
        if (type != SshAuthenticationType.ClientPublicKey)
        {
            result = ConnectionResult.InvalidMethod;
            return null;
        }

        // check if key is known - no host check
        if (!trustedCerts.HasPlainKey(publicKey))
        {
            result = ConnectionResult.UnknownHostOrKey;
            return null;
        }

        // all checks successful
        result = ConnectionResult.Success;
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, host ?? "")], "SSH2"));
    }

    public static ClaimsPrincipal? ClientValidateRemote(
        string host, IKeyPair? publicKey, SshAuthenticationType type, TrustStore trustedCerts,
        out ConnectionResult result
    )
    {
        result = ConnectionResult.UnspecifiedError;

        // assert proper method
        if (type != SshAuthenticationType.ServerPublicKey)
        {
            result = ConnectionResult.InvalidMethod;
            return null;
        }

        // check public key hostname
        if (!trustedCerts.IsKnown(host))
        {
            result = ConnectionResult.UnknownHostOrKey;
            return null;
        }

        if (!trustedCerts.IsPublicKeyValid(host, publicKey))
        {
            result = ConnectionResult.MismatchedPublicKey;
            return null;
        }

        // all checks successful
        result = ConnectionResult.Success;
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, host)]));
    }

    /// <summary>
    ///     Communicates with the specified remote host to get rejection reasons over an unsecured channel.
    ///     The local host will both tell the remote host why it rejected the connection, and return a
    ///     reason provided by the remote host (both if applicable).
    /// </summary>
    /// <param name="client">the client to communicate over</param>
    /// <param name="result">the result of authentication on this host</param>
    /// <param name="ct">the token to cancel this asynchronous operation</param>
    /// <returns></returns>
    public static async Task<RejectionReason> CommunicateRejectionAsync(
        TcpClient client, ConnectionResult result, CancellationToken ct = default
    )
    {
        try
        {
            await client.GetStream().WriteAsync(new[]
            {
                (byte)(result switch
                {
                    ConnectionResult.UnspecifiedError => RejectionReason.UnspecifiedError,
                    ConnectionResult.BadPublicKey => RejectionReason.BadPublicKey,
                    ConnectionResult.MismatchedPublicKey => RejectionReason.MismatchedPublicKey,
                    ConnectionResult.UnknownHostOrKey => RejectionReason.UnknownHostOrKey,
                    ConnectionResult.InvalidMethod => RejectionReason.InvalidMethod,
                    ConnectionResult.Success => RejectionReason.None,
                    _ => RejectionReason.UnspecifiedError,
                }),
            }, ct);

            // see if there's a reason (defaults to None)
            byte[] reason = new byte[1];
            int processed = await client.GetStream().ReadAsync(reason, ct);

            // make sure stream isn't closed yet
            if (processed > 0)
            {
                return (RejectionReason)reason[0];
            }

            return RejectionReason.CouldNotGetReason;
        }
        catch (IOException)
        {
            return RejectionReason.CouldNotGetReason;
        }
    }

    public struct ConnectionInfo
    {
        public required ConnectionResult Result;
        public required RejectionReason Reason;
        public required string Hostname;
        public required SecureConnection? Connection;
        public required IKeyPair? PublicKey;
    }
}