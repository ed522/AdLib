using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;

using AdLib.IO;
using AdLib.Tests.Ssh;

using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;

// ReSharper disable MethodHasAsyncOverload
// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace AdLib.Tests.Communications;

/// <summary>
///     Tests for <see cref="SshDataBlockChannel" /> that run on top of a real functional <see cref="SshChannel" />,
///     obtained through an in-process SSH connection (<see cref="SecureClient" /> and <see cref="SecureServer" />).
/// </summary>
[TestFixture]
public class SshBlockChannelTests : SecureCommsTestsBase
{
    private const int ReceiveTimeoutMs = 1000;

    private readonly IKeyPair _keys = ECDsa.KeyPair.Generate(ECDsa.ECDsaSha2Nistp256);

    private static readonly byte[] SmallPayload = [0x00, 0x01, 0x02, 0xFF];
    private static readonly byte[] LargePayload = CreateLargePayload();

    [OneTimeTearDown]
    public void DisposeTheKey() { this._keys.Dispose(); }

    [Test]
    public async Task TestFunctionalSshChannel_RoundTripsDataThroughBlockChannel()
    {
        var (serverConnection, clientConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel serverChannel = new(serverConnection);
        SshDataBlockChannel clientChannel = new(clientConnection);

        byte[] payload = [0x00, 0x01, 0x02, 0xFF];

        serverChannel.PutBlock(payload);
        byte[] received = clientChannel.GetBlock();

        Assert.That(received, Is.EquivalentTo(payload));
    }

    // ===== SshDataBlockChannel write contract =====

    [Test]
    public async Task PutBlock_SendsBlockToRemoteChannel()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(SmallPayload);
        byte[] received = clientChannel.GetBlock();

        Assert.That(received, Is.EquivalentTo(SmallPayload));
    }

    [Test]
    public async Task PutBlockAsync_SendsBlockToRemoteChannel()
    {
        Console.WriteLine("x");
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        Console.WriteLine("y");

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);
        Console.WriteLine("z");

        await clientChannel.PutBlockAsync(SmallPayload);
        Console.WriteLine("w");
        byte[] received = serverChannel.GetBlock();

        Assert.That(received, Is.EquivalentTo(SmallPayload));
    }

    [Test]
    public async Task PutBlock_EmptyData_DoesNotSendBlockToRemoteChannel()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        clientChannel.PutBlock(Array.Empty<byte>());

        Assert.Multiple(() =>
        {
            Assert.That(serverChannel.HasBlock(), Is.False);
            Assert.That(serverChannel.TryGetBlockImmediately(out byte[]? block), Is.False);
            Assert.That(block, Is.Null);
        });
    }

    // ===== SshDataBlockChannel read contract =====

    [Test]
    public async Task GetBlockImmediately_ReturnsNullWhenNoBlockAvailable()
    {
        var (clientConnection, _, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);

        Assert.That(clientChannel.GetBlockImmediately(), Is.Null);
    }

    [Test]
    public async Task GetBlockImmediately_ReturnsBlockWhenAvailable()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(SmallPayload);
        await WaitUntilAsync(clientChannel.HasBlock);

        byte[]? received = clientChannel.GetBlockImmediately();

        Assert.That(received, Is.Not.Null);
        Assert.That(received!, Is.EquivalentTo(SmallPayload));
    }

    [Test]
    public async Task HasBlock_IsFalseWhenNoBlockAvailable()
    {
        var (clientConnection, _, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);

        Assert.That(clientChannel.HasBlock(), Is.False);
    }

    [Test]
    public async Task HasBlock_IsTrueWhenBlockAvailable()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(SmallPayload);
        await WaitUntilAsync(clientChannel.HasBlock);

        Assert.That(clientChannel.HasBlock(), Is.True);

        byte[] received = clientChannel.GetBlock();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received, Is.EquivalentTo(SmallPayload));
            Assert.That(clientChannel.HasBlock(), Is.False);
        }
    }

    [Test]
    public async Task TryGetBlockImmediately_ReturnsFalseWithNullOutWhenNoBlockAvailable()
    {
        var (clientConnection, _, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientChannel.TryGetBlockImmediately(out byte[]? block), Is.False);
            Assert.That(block, Is.Null);
        }
    }

    [Test]
    public async Task TryGetBlockImmediately_ReturnsTrueWithBlockWhenAvailable()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(SmallPayload);
        await WaitUntilAsync(clientChannel.HasBlock);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientChannel.TryGetBlockImmediately(out byte[]? block), Is.True);
            Assert.That(block, Is.Not.Null);
            Assert.That(block!, Is.EquivalentTo(SmallPayload));
        }
    }

    [Test]
    public async Task GetBlock_ReturnsNullWhenNoBlockAvailableWithinTimeout()
    {
        var (clientConnection, _, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);

        Assert.That(clientChannel.GetBlock(ReceiveTimeoutMs), Is.Null);
    }

    [Test, CancelAfter(1000), MaxTime(1000)]
    public async Task GetBlock_BlocksUntilBlockAvailable(CancellationToken ct)
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var unused = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        bool waitOver = false;

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, ct);
            serverChannel.PutBlock(SmallPayload);
            waitOver = true;
        }, ct);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientChannel.GetBlock(), Is.Not.Null);
            Assert.That(waitOver, Is.True);
        }
    }

    [Test]
    public async Task GetBlockAsync_ReturnsBlockWhenAvailable()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(SmallPayload);
        byte[] received = await clientChannel.GetBlockAsync();

        Assert.That(received, Is.EquivalentTo(SmallPayload));
    }

    [Test]
    public async Task GetBlockAsync_ReturnsCancelledTaskWhenTokenCancelled()
    {
        var (clientConnection, _, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);

        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        var getTask = clientChannel.GetBlockAsync(cancellationTokenSource.Token);

        try
        {
            await getTask;
            Assert.Fail("Expected GetBlockAsync to throw a cancellation exception");
        }
        catch (TaskCanceledException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public async Task MessageReceived_FiresWhenBlockReceived()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        TaskCompletionSource<bool> receivedTask = new();
        clientChannel.MessageReceived += (_, _) => { receivedTask.TrySetResult(true); };

        serverChannel.PutBlock(SmallPayload);
        await Task.Delay(1000);

        Assert.That(receivedTask.Task.IsCompleted, Is.True);
    }

    // ===== Roundtrips =====

    [Test]
    public async Task RoundTrip_LargeBlock_PreservesDataThroughSplitBlocks()
    {
        var (clientConnection, serverConnection, disposable) = await this.ConnectHosts();
        using var _ = disposable;

        SshDataBlockChannel clientChannel = new(clientConnection);
        SshDataBlockChannel serverChannel = new(serverConnection);

        serverChannel.PutBlock(LargePayload);
        byte[] received = clientChannel.GetBlock();

        Assert.That(received.SequenceEqual(LargePayload), Is.True);
    }

    private static byte[] CreateLargePayload()
    {
        byte[] payload = new byte[100_000];

        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i * 31);
        }

        return payload;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        for (int i = 0; i < timeoutMs; i += 10)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within the allotted timeout");
    }

    private async Task<(SshChannel client, SshChannel server, IDisposable disposable)> ConnectHosts()
    {
        TcpListener tcpServer = new(IPAddress.Any, 0);
        tcpServer.Start();
        var serverTcpTask = tcpServer.AcceptTcpClientAsync();
        TcpClient clientTcp = new();
        var clientTcpTask = clientTcp.ConnectAsync("localhost", ((IPEndPoint)tcpServer.LocalEndpoint).Port);

        var serverTcp = await serverTcpTask;
        await clientTcpTask;

        var serverTask = ConnectServer();
        var clientTask = ConnectClient();

        await Task.WhenAll(serverTask, clientTask);

        var client = await clientTask;
        var server = await serverTask;

        if (client is null || server is null)
        {
            Assert.Inconclusive("Failed to connect SSH channels");
        }

        return (client, server, new MultiDisposable(tcpServer, serverTcp, clientTcp, client, server));

        async Task<SshChannel?> ConnectServer(CancellationToken ct = default)
        {
            TaskCompletionSource<bool> authCompleteTask = new();
            SshSessionConfiguration config = new();
            SshServerSession session = new(config, new TraceSource("AdLib_SshServer"));

            session.Credentials = new SshServerCredentials(this._keys);

            session.Authenticating += (_, args) =>
            {
                args.AuthenticationTask = Task.FromResult(new ClaimsPrincipal());
                authCompleteTask.TrySetResult(true);
            };

            await session.ConnectAsync(serverTcp.GetStream(), ct);

            try
            {
                if (await authCompleteTask.Task)
                {
                    return await session.AcceptChannelAsync(ct);
                }

                // ????
                return null;
            }
            catch (SshConnectionException)
            {
                return null;
            }
        }

        async Task<SshChannel?> ConnectClient(CancellationToken ct = default)
        {
            SshSessionConfiguration config = new();
            SshClientCredentials clientCreds = new();
            SshClientSession session = new(config, new TraceSource("AdLib_SshClient"));

            await session.ConnectAsync(clientTcp.GetStream(), ct);

            session.Authenticating += (_, args) =>
            {
                // no idea what the null analysis thing is on about
                args.AuthenticationTask = Task.FromResult(new ClaimsPrincipal());
            };

            try
            {
                if (await session.AuthenticateAsync(clientCreds, ct))
                {
                    return await session.OpenChannelAsync(ct);
                }

                // ????
                return null;
            }
            catch (SshConnectionException)
            {
                Assert.Inconclusive("Unhandled exception in connection setup");
            }

            return null;
        }
    }

    private class MultiDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public MultiDisposable(params IDisposable[] disposables) => this._disposables = disposables;

        public void Dispose()
        {
            foreach (var d in this._disposables)
            {
                d.Dispose();
            }
        }
    }
}