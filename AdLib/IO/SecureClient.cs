using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using AdLib.Identities;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.IO;

public sealed class SecureClient(Identity identity, TrustStore trustedCerts) : IDisposable
{
    private const int RecoveryTimeout = 10;

    private SecureConnection? _connection;

    public SshChannel? Channel => this._connection?.Channel;

    public void Dispose() { this._connection?.Dispose(); }

    public async Task<ConnectionInfo> ConnectAsync(string host, CancellationToken ct = default)
    {
        TcpClient tcpClient = new();
        await tcpClient.ConnectAsync(host, Port, ct);

        ConnectionResult result = ConnectionResult.DidNotAttempt;
        RejectionReason reason = RejectionReason.None;

        SshSessionConfiguration config = new();
        SshClientCredentials clientCreds = new(identity.InternalName.ToString(), identity.Keys);

        SshClientSession session = new(config, new TraceSource("AdLib_SshClient"));
        TaskCompletionSource<IKeyPair?> keyTask = new();

        await session.ConnectAsync(tcpClient.GetStream(), ct);

        session.Authenticating += (_, args) =>
        {
            ClaimsPrincipal? value = ClientValidateRemote(
                host, args.PublicKey, args.AuthenticationType, trustedCerts, out result
            );

            keyTask.SetResult(args.PublicKey);
            args.AuthenticationTask = Task.FromResult(value);
        };

        if (await session.AuthenticateAsync(clientCreds, ct))
        {
            SshChannel channel = await session.OpenChannelAsync(ct);
            // cert + result are now set, unless validator never ran
            this._connection = new SecureConnection(tcpClient, channel);
        }
        else
        {
            // clean up (since the streams are invalid now)
            this._connection = null;
            // if we didn't even get far enough to authenticate then indicate there is no key
            if (!keyTask.Task.IsCompleted) keyTask.TrySetResult(null);

            await session.CloseAsync(SshDisconnectReason.ByApplication, "Authentication failed");

            session.Dispose();
            tcpClient.Dispose();

            CancellationTokenSource recoveryConnectTimeout = new(TimeSpan.FromSeconds(RecoveryTimeout));
            await using CancellationTokenRegistration _ = ct.Register(recoveryConnectTimeout.Cancel);

            // since we need to reconnect, if the host becomes completely unavailable, don't keep trying for forever
            try
            {
                using TcpClient recoveryClient = new();
                await recoveryClient.ConnectAsync(host, RecoveryPort, recoveryConnectTimeout.Token);
                reason = await CommunicateRejectionAsync(recoveryClient, result, recoveryConnectTimeout.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
                reason = RejectionReason.CouldNotGetReason;
            }
        }

        IKeyPair? pair = await keyTask.Task;

        return new ConnectionInfo
        {
            Result = result,
            Hostname = host,
            Connection = this._connection,
            PublicKey = pair,
            Reason = reason,
        };
    }
}