using System.Net;
using System.Net.Sockets;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.Tests.Ssh;

[TestFixture]
public class SecureCommsRejectionCommunicationTests
{
    [SetUp]
    public void Setup()
    {
        this._listener.Stop();
        this._listener.Start();
        this._client = new TcpClient();
        this._client.Connect(ServerAddr, Port);
        this._server = this._listener.AcceptTcpClient();
    }

    [TearDown]
    public void CleanUp()
    {
        this._client.Dispose();
        this._server.Dispose();
        this._listener.Stop();
    }

    private static readonly IPAddress ServerAddr = IPAddress.Loopback;

    private readonly TcpListener _listener = new(ServerAddr, Port);
    private TcpClient _client;
    private TcpClient _server;

    private static IEnumerable<object[]> RejectionReasonTestCases()
    {
        yield return [ConnectionResult.DidNotAttempt, RejectionReason.UnspecifiedError];
        yield return [ConnectionResult.Success, RejectionReason.None];
        yield return [ConnectionResult.BadPublicKey, RejectionReason.BadPublicKey];
        yield return [ConnectionResult.UnknownHostOrKey, RejectionReason.UnknownHostOrKey];
        yield return [ConnectionResult.MismatchedPublicKey, RejectionReason.MismatchedPublicKey];
        yield return [ConnectionResult.UnspecifiedError, RejectionReason.UnspecifiedError];
    }

    [TestCaseSource(nameof(RejectionReasonTestCases))]
    public async Task TestReasonWrittenToStream_MatchesExpected(
        ConnectionResult inputResult, RejectionReason expectedReason
    )
    {
        // make sure that the client can exit cleanly
        this._server.GetStream().WriteByte((byte)RejectionReason.None);

        await CommunicateRejectionAsync(this._client, inputResult);
        Assert.That(this._server.GetStream().ReadByte(), Is.EqualTo((byte)expectedReason));
    }

    [Test]
    public async Task TestReturnedReasonOnClosedStream_IsUnspecifiedError()
    {
        // indicates EOF
        this._server.Client.Shutdown(SocketShutdown.Send);

        RejectionReason actual = await CommunicateRejectionAsync(this._client, ConnectionResult.Success);
        Assert.That(actual, Is.EqualTo(RejectionReason.UnspecifiedError));
    }
}