using System.Net;
using System.Net.Sockets;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.Tests.Ssh;

[TestFixture]
public class SecureCommunicationPreAuthTests
{
    private static readonly IPAddress ServerAddr = IPAddress.Loopback;

    private readonly TcpListener _listener = new(ServerAddr, Port);
    private TcpClient _client;
    private TcpClient _server;

    private readonly byte[] _serverFingerprint = new byte[32];
    private readonly byte[] _clientFingerprint = new byte[32];

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        Random rand = new();
        rand.NextBytes(this._serverFingerprint);
        rand.NextBytes(this._clientFingerprint);
    }

    [SetUp]
    public async Task Setup()
    {
        this._listener.Stop();
        this._listener.Start();
        this._client = new TcpClient();

        Task clientConnectTask = Task.Run(async () => await this._client.ConnectAsync(ServerAddr, Port));
        Task<TcpClient> serverAcceptTask = Task.Run(async () => await this._listener.AcceptTcpClientAsync());
        await Task.WhenAll(clientConnectTask, serverAcceptTask);

        this._server = await serverAcceptTask;
        await clientConnectTask;
    }

    [TearDown]
    public void CleanUp()
    {
        this._client.Dispose();
        this._server.Dispose();
        this._listener.Stop();
        this._listener.Dispose();
    }

    private Task CreateConnections(
        out Task<PreAuthInfo> clientInfoTask, out Task<PreAuthInfo> serverInfoTask,
        byte[] clientPublicKeyFingerprint, Func<byte[], ConnectionResult> clientPreCheckAction,
        byte[] serverPublicKeyFingerprint, Func<byte[], ConnectionResult> serverPreCheckAction
    )
    {
        clientInfoTask = ExchangePreAuthInfoAsync(this._client, clientPublicKeyFingerprint, clientPreCheckAction);
        serverInfoTask = ExchangePreAuthInfoAsync(this._server, serverPublicKeyFingerprint, serverPreCheckAction);
        return Task.WhenAll(clientInfoTask, serverInfoTask);
    }

    private static IEnumerable<object[]> RejectionReasonTestCases()
    {
        yield return [ConnectionResult.DidNotAttempt, RejectionReason.UnspecifiedError];
        yield return [ConnectionResult.Success, RejectionReason.None];
        yield return [ConnectionResult.BadPublicKey, RejectionReason.BadPublicKey];
        yield return [ConnectionResult.UnknownHostOrKey, RejectionReason.UnknownHostOrKey];
        yield return [ConnectionResult.MismatchedPublicKey, RejectionReason.MismatchedPublicKey];
        yield return [ConnectionResult.UnspecifiedError, RejectionReason.UnspecifiedError];
    }

    [Test, TestCaseSource(nameof(RejectionReasonTestCases))]
    public async Task TestPreAuthRejectionCommunication_CorrectOutput(ConnectionResult result, RejectionReason reason)
    {
        await this.CreateConnections(
            out Task<PreAuthInfo> clientInfoTask, out Task<PreAuthInfo> serverInfoTask,
            this._clientFingerprint, _ => result, this._serverFingerprint, _ => result
        );

        PreAuthInfo clientInfo = await clientInfoTask;
        PreAuthInfo serverInfo = await serverInfoTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientInfo.RejectionReason, Is.EqualTo(reason));
            Assert.That(serverInfo.RejectionReason, Is.EqualTo(reason));
        }
    }

    [Test]
    public async Task TestPreAuthFingerprintOutput_NotNull()
    {
        await this.CreateConnections(
            out Task<PreAuthInfo> clientInfoTask, out Task<PreAuthInfo> serverInfoTask,
            this._clientFingerprint, _ => ConnectionResult.Success,
            this._serverFingerprint, _ => ConnectionResult.Success
        );

        PreAuthInfo clientInfo = await clientInfoTask;
        PreAuthInfo serverInfo = await serverInfoTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientInfo.PublicKeyFingerprint, Is.Not.Null);
            Assert.That(serverInfo.PublicKeyFingerprint, Is.Not.Null);
        }
    }

    [Test]
    public async Task TestPreAuthFingerprintOutput_CorrectOutput()
    {
        byte[] receivedServerFingerprint = [];
        byte[] receivedClientFingerprint = [];

        await this.CreateConnections(
            out Task<PreAuthInfo> clientInfoTask, out Task<PreAuthInfo> serverInfoTask,
            this._clientFingerprint,
            fingerprint =>
            {
                receivedServerFingerprint = fingerprint;
                return ConnectionResult.Success;
            },
            this._serverFingerprint,
            fingerprint =>
            {
                receivedClientFingerprint = fingerprint;
                return ConnectionResult.Success;
            }
        );

        PreAuthInfo clientInfo = await clientInfoTask;
        PreAuthInfo serverInfo = await serverInfoTask;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                receivedClientFingerprint.AsSpan().SequenceEqual(this._clientFingerprint.AsSpan()),
                Is.True
            );

            Assert.That(
                receivedServerFingerprint.AsSpan().SequenceEqual(this._serverFingerprint.AsSpan()),
                Is.True
            );

            Assert.That(
                clientInfo.PublicKeyFingerprint.AsSpan().SequenceEqual(this._serverFingerprint.AsSpan()),
                Is.True
            );

            Assert.That(
                serverInfo.PublicKeyFingerprint.AsSpan().SequenceEqual(this._clientFingerprint.AsSpan()),
                Is.True
            );
        }
    }

    [Test]
    public async Task TestPreAuthPrematureClose_ReturnsCouldNotGetReason_NullKey()
    {
        this._client.Close();

        PreAuthInfo serverResult = await ExchangePreAuthInfoAsync(
            this._server,
            this._serverFingerprint,
            _ => ConnectionResult.Success
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(serverResult.RejectionReason, Is.EqualTo(RejectionReason.CouldNotGetReason));
            Assert.That(serverResult.PublicKeyFingerprint, Is.Null);
        }
    }
}