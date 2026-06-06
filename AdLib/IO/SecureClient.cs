using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;
using Microsoft.DevTunnels.Ssh.Events;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.IO;

public sealed class SecureClient(Identity identity, TrustStore trustedCerts) : IDisposable
{
    private SecureConnection? _connection;

    public SshChannel? Channel => this._connection?.Channel;

    public void Dispose() { this._connection?.Dispose(); }

    private ConnectionResult ValidatePreAuthRemote(string host, byte[] fingerprint)
    {
        if (!trustedCerts.IsKnown(host)) return ConnectionResult.UnknownHostOrKey;
        if (!trustedCerts.IsFingerprintValid(host, fingerprint)) return ConnectionResult.MismatchedPublicKey;
        // NOTE: this does not mean that they are authenticated, just that authentication can proceed
        return ConnectionResult.Success;
    }

    public async Task<ConnectionInfo> ConnectAsync(string host, CancellationToken ct = default)
    {
        TcpClient tcpClient = new();
        await tcpClient.ConnectAsync(host, Port, ct);

        ConnectionResult result = ConnectionResult.DidNotAttempt;
        RejectionReason reason = RejectionReason.None;

        // cooperative authentication over raw tcp first
        // does not influence actual security decisions, but guarantees that both parties can see each other's keys
        // if someone gives a fake key, it fails at SSH auth with possibly unspecified error messages
        bool needsToClose = false;
        byte[] remoteFingerprint = [];
        TaskCompletionSource<IKeyPair?> keyTask = new();

        try
        {
            PreAuthInfo info = await ExchangePreAuthInfoAsync(
                tcpClient,
                PublicKeyInfo.GetCanonicalFingerprint(identity.Keys),
                fingerprint =>
                {
                    remoteFingerprint = fingerprint[..];
                    // preemptive result - overriden if the pre-auth succeeds
                    result = this.ValidatePreAuthRemote(host, fingerprint);
                    return result;
                },
                ct
            );

            if (info.RejectionReason != RejectionReason.None || result != ConnectionResult.Success)
            {
                reason = info.RejectionReason;
                needsToClose = true;
            }
            else
            {
                SshSessionConfiguration config = new();
                SshClientCredentials clientCreds = new(identity.InternalName.ToString(), identity.Keys);
                SshClientSession session = new(config, new TraceSource("AdLib_SshClient"));

                await session.ConnectAsync(tcpClient.GetStream(), ct);

                session.Authenticating += (_, args) =>
                {
                    ClaimsPrincipal? value = ClientValidateRemote(
                        host, args.PublicKey, args.AuthenticationType, trustedCerts, out result
                    );

                    keyTask.SetResult(args.PublicKey);
                    args.AuthenticationTask = Task.FromResult(value);
                };

                try
                {
                    if (await session.AuthenticateAsync(clientCreds, ct))
                    {
                        SshChannel channel = await session.OpenChannelAsync(ct);
                        // cert + result are now set, unless validator never ran
                        this._connection = new SecureConnection(tcpClient, channel);
                        needsToClose = false;
                    }
                    else
                    {
                        // give up (there's no point in recovering if the other side is going to lie about its key)
                        this._connection = null;
                        if (!keyTask.Task.IsCompleted) keyTask.TrySetResult(null);

                        await session.CloseAsync(SshDisconnectReason.ByApplication, "Authentication failed");
                        session.Dispose();
                        needsToClose = true;
                    }
                }
                catch (SshConnectionException)
                {
                    needsToClose = true;
                    session.Dispose();
                }
            }
        }
        finally
        {
            // always have some result set even if it failed to avoid deadlock
            if (!keyTask.Task.IsCompleted) keyTask.TrySetResult(null);
            if (needsToClose) tcpClient.Dispose();
        }

        IKeyPair? pair = await keyTask.Task;

        return new ConnectionInfo
        {
            Result = result,
            Reason = reason,
            Hostname = host,
            Connection = this._connection,
            PublicKey = pair,
            PublicKeyFingerprint = remoteFingerprint,
        };
    }

    internal static ClaimsPrincipal? ClientValidateRemote(
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
        ClaimsIdentity claimsIdentity = new([new Claim(ClaimTypes.Name, host)], "SSH2");
        return new ClaimsPrincipal(claimsIdentity);
    }
}