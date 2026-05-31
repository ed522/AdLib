using System.Net;
using System.Net.Sockets;

using static AdLib.IO.TlsUtils;

namespace AdLib.Tests.Tls;

[TestFixture]
public class TlsRejectionCommunicationTests
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
        yield return [ConnectionResult.BadCertificate, RejectionReason.BadCertificate];
        yield return [ConnectionResult.UntrustedCertificate, RejectionReason.UntrustedCertificate];
        yield return [ConnectionResult.MismatchedCertificate, RejectionReason.MismatchedCertificate];
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