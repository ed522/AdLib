using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;

using static AdLib.IO.SecureConnectionUtils;

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
        this._listener = new TcpListener(IPAddress.Any, Port);
        this._trustStore = trustedCerts ?? new TrustStore();
    }

    public SecureServer(Identity identity, PublicKeyInfo[]? trustedCerts = null) :
        this(identity, new TrustStore(trustedCerts))
    {
        // empty
    }

    public void Dispose()
    {
        if (this._disposed) return;
        this._listener.Stop();
        this._listener.Dispose();
        this._disposed = true;
    }

    public void Start() => this._listener.Start();

    public void Stop() => this._listener.Stop();

    private ConnectionResult ValidatePreAuthRemote(ReadOnlySpan<byte> fingerprint)
    {
        if (!this._trustStore.HasPlainFingerprint(fingerprint)) return ConnectionResult.UnknownHostOrKey;
        return ConnectionResult.Success;
    }

    public async Task<ConnectionInfo> AcceptClientAsync(CancellationToken ct = default)
    {
        TcpClient tcpClient = await this._listener.AcceptTcpClientAsync(ct);
        IPAddress? ip = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;

        SecureConnection? connection = null;
        RejectionReason reason = RejectionReason.None;
        ConnectionResult result = ConnectionResult.DidNotAttempt;

        TaskCompletionSource<IKeyPair?> authAttemptedTask = new();
        TaskCompletionSource<bool> authCompleteTask = new();
        SshSessionConfiguration config = new();
        SshServerSession session = new(config, new TraceSource("AdLib_SshClient"));

        bool needsToClose = false;
        byte[] remoteFingerprint = [];

        try
        {
            PreAuthInfo info = await ExchangePreAuthInfoAsync(
                tcpClient,
                PublicKeyInfo.GetCanonicalFingerprint(this._identity.Keys),
                fingerprint =>
                {
                    remoteFingerprint = fingerprint[..];
                    // preemptive result - overriden if the pre-auth succeeds
                    result = this.ValidatePreAuthRemote(fingerprint);
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
                session.Credentials = new SshServerCredentials(this._identity.Keys);

                session.Authenticating += (_, args) =>
                {
                    authAttemptedTask.TrySetResult(args.PublicKey);

                    ClaimsPrincipal? ret = ServerValidateRemote(
                        ip?.ToString() ?? "", args.PublicKey, args.AuthenticationType,
                        this._trustStore, out result
                    );

                    authCompleteTask.TrySetResult(ret is not null);
                    args.AuthenticationTask = Task.FromResult(ret);
                };

                session.Closed += (_, _) =>
                {
                    if (!authAttemptedTask.Task.IsCompleted) authAttemptedTask.TrySetResult(null);
                    authCompleteTask.TrySetResult(false);
                };

                await session.ConnectAsync(tcpClient.GetStream(), ct);

                try
                {
                    if (await authCompleteTask.Task)
                    {
                        SshChannel channel = await session.AcceptChannelAsync(ct);
                        connection = new SecureConnection(tcpClient, channel);
                    }
                    else
                    {
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
        catch
        {
            needsToClose = true;
            throw;
        }
        finally
        {
            if (!authAttemptedTask.Task.IsCompleted) authAttemptedTask.TrySetResult(null);

            if (needsToClose)
            {
                session.Dispose();
                tcpClient.Dispose();
                connection = null;
            }
        }

        return new ConnectionInfo
        {
            Result = result,
            Reason = reason,
            Hostname = ip?.ToString() ?? "<unknown>",
            Connection = connection,
            PublicKey = await authAttemptedTask.Task,
            PublicKeyFingerprint = remoteFingerprint,
        };
    }
}