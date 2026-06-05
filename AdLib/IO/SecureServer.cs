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

namespace AdLib.IO;

public sealed class SecureServer : IDisposable
{
    private const int RecoveryTimeoutSeconds = 10;
    
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

    public async Task<SecureConnectionUtils.ConnectionInfo> AcceptClientAsync(CancellationToken ct = default)
    {
        TcpClient tcpClient = await this._listener.AcceptTcpClientAsync(ct);
        IPAddress? ip = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;

        using TcpListener recoveryListener = new(IPAddress.Any, SecureConnectionUtils.RecoveryPort);
        recoveryListener.Start();

        SecureConnection? connection = null;
        SecureConnectionUtils.RejectionReason reason = SecureConnectionUtils.RejectionReason.None;
        SecureConnectionUtils.ConnectionResult result = SecureConnectionUtils.ConnectionResult.DidNotAttempt;

        TaskCompletionSource<IKeyPair?> authAttemptedTask = new();
        SshSessionConfiguration config = new();
        SshServerSession session = new(config, new TraceSource("AdLib_SshClient"));
        session.Credentials = new SshServerCredentials(this._identity.Keys);
        TaskCompletionSource<bool> authCompleteTask = new();

        try
        {
            session.Authenticating += (_, args) =>
            {
                authAttemptedTask.TrySetResult(args.PublicKey);

                ClaimsPrincipal? ret = SecureConnectionUtils.ServerValidateRemote(
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

            bool shouldRecover;

            try
            {
                if (await authCompleteTask.Task)
                {
                    SshChannel channel = await session.AcceptChannelAsync(ct);
                    connection = new SecureConnection(tcpClient, channel);
                    shouldRecover = false;
                }
                else
                {
                    shouldRecover = true;
                }
            }
            catch (SshConnectionException)
            {
                shouldRecover = true;
            }

            if (shouldRecover)
            {
                if (!authAttemptedTask.Task.IsCompleted) authAttemptedTask.TrySetResult(null);

                CancellationTokenSource timeout = new(TimeSpan.FromSeconds(RecoveryTimeoutSeconds));
                ct.Register(timeout.Cancel);

                try
                {
                    using TcpClient recoveryClient = await recoveryListener.AcceptTcpClientAsync(timeout.Token);

                    reason = await SecureConnectionUtils.CommunicateRejectionAsync(recoveryClient, result,
                        timeout.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or TimeoutException)
                {
                    reason = SecureConnectionUtils.RejectionReason.CouldNotGetReason;
                }

                tcpClient.Dispose();
                session.Dispose();
                connection = null;
            }
        }
        catch
        {
            session.Dispose();
            tcpClient.Dispose();
            throw;
        }

        return new SecureConnectionUtils.ConnectionInfo
        {
            Result = result,
            Reason = reason,
            Hostname = ip?.ToString() ?? "<unknown>",
            Connection = connection,
            PublicKey = await authAttemptedTask.Task,
        };
    }
}