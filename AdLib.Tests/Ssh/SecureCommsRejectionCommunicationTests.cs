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

    // TODO reimplement
}