using AdLib.Identities;
using AdLib.IO;

using static AdLib.IO.SecureConnectionUtils;

namespace AdLib.Tests.Ssh;

[NonParallelizable]
public class SecureConnectionTests : SecureCommsTestsBase
{
    private readonly SemaphoreSlim _addressBindSemaphore = new(1, 1);

    private static async Task<Connections> ConnectHosts(
        Identity clientIdentity, TrustStore clientTrustStore,
        Identity serverIdentity, TrustStore serverTrustStore
    )
    {
        CancellationToken token = CancellationToken.None;
        SecureClient client = new(clientIdentity, clientTrustStore);
        SecureServer server = new(serverIdentity, serverTrustStore);
        server.Start();

        Task<ConnectionInfo> connectToServerTask = Task.Run(
            async () => await client.ConnectAsync(Host, token), token
        );

        Task<ConnectionInfo> acceptClientTask = Task.Run(
            async () => await server.AcceptClientAsync(token), token
        );

        // make sure at least one succeeds before awaiting either, since it could deadlock
        Task.WaitAny([connectToServerTask, acceptClientTask], token);

        return new Connections
        {
            ConnectionToServer = await connectToServerTask,
            ConnectionToClient = await acceptClientTask,
            SecureClient = client,
            SecureServer = server,
        };
    }

    [Test]
    public async Task TestClientConnectWithTrustedCertificate_Success()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, this.ClientTrustStore,
                this.ServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo _, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToServer.Result, Is.EqualTo(ConnectionResult.Success));
                Assert.That(connectionToServer.Reason, Is.EqualTo(RejectionReason.None));
                Assert.That(connectionToServer.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToServer.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToServer.PublicKey, Is.Not.Null);
                Assert.That(connectionToServer.Connection, Is.Not.Null);
                Assert.That(connectionToServer.Hostname, Is.Not.Null);
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestClientConnectWithTrustedCertificate_HasCorrectInfo()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, this.ClientTrustStore,
                this.ServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo _, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToServer.Hostname, Is.EqualTo(Host));
                Assert.That(connectionToServer.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToServer.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToServer.PublicKey, Is.Not.Null);

                Assert.That(this.ClientTrustStore.IsKnown(Host), Is.True);
                Assert.That(this.ClientTrustStore.IsPublicKeyValid(Host, connectionToServer.PublicKey), Is.True);

                Assert.That(
                    connectionToServer.PublicKey?
                                      .GetPublicKeyBytes()
                                      .SequenceEqual(this.ServerIdentity.Keys.GetPublicKeyBytes()),
                    Is.True
                );
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestClientConnectWithUntrustedServerCertificate_Fails()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, new TrustStore(),
                this.UntrustedServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToServer.Hostname, Is.EqualTo(Host));
                Assert.That(connectionToServer.Result, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
                Assert.That(connectionToServer.Reason, Is.EqualTo(RejectionReason.None));
                Assert.That(connectionToClient.Reason, Is.EqualTo(RejectionReason.UnknownHostOrKey));
                Assert.That(connectionToServer.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToServer.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToServer.Connection, Is.Null);
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestClientConnectWithMismatchedServerCertificate_Fails()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, this.ClientTrustStore,
                this.UntrustedServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToServer.Hostname, Is.EqualTo(Host));
                Assert.That(connectionToServer.Result, Is.EqualTo(ConnectionResult.MismatchedPublicKey));
                Assert.That(connectionToServer.Reason, Is.EqualTo(RejectionReason.None));
                Assert.That(connectionToClient.Reason, Is.EqualTo(RejectionReason.MismatchedPublicKey));
                Assert.That(connectionToServer.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToServer.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToServer.Connection, Is.Null);
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestServerConnectWithTrustedCertificate_Success()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, this.ClientTrustStore,
                this.ServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo _) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToClient.Result, Is.EqualTo(ConnectionResult.Success));
                Assert.That(connectionToClient.Reason, Is.EqualTo(RejectionReason.None));
                Assert.That(connectionToClient.Connection, Is.Not.Null);
                Assert.That(connectionToClient.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToClient.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToClient.PublicKey, Is.Not.Null);
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestServerConnectWithTrustedCertificate_HasCorrectInfo()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.ClientIdentity, this.ClientTrustStore,
                this.ServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo _) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToClient.Hostname, Is.EqualTo(Host));
                Assert.That(connectionToClient.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToClient.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToClient.PublicKey, Is.Not.Null);

                Assert.That(
                    this.ServerTrustStore.HasPlainKey(connectionToClient.PublicKey),
                    Is.True
                );

                Assert.That(
                    connectionToClient.PublicKey?
                                      .GetPublicKeyBytes().Span
                                      .SequenceEqual(this.ClientIdentity.Keys.GetPublicKeyBytes().Span),
                    Is.True
                );
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestServerConnectWithUntrustedClientCertificate_Fails()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            using Connections connections = await ConnectHosts(
                this.UntrustedClientIdentity, this.ClientTrustStore,
                this.ServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToClient.Result, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
                Assert.That(connectionToClient.Reason, Is.EqualTo(RejectionReason.None));
                Assert.That(connectionToServer.Reason, Is.EqualTo(RejectionReason.UnknownHostOrKey));
                Assert.That(connectionToClient.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToClient.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToClient.Connection, Is.Null);
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    [Test]
    public async Task TestDoubleUntrustedConnect_Fails()
    {
        await this._addressBindSemaphore.WaitAsync();

        try
        {
            // cannot reuse trust store since it gives the wrong error
            using Connections connections = await ConnectHosts(
                this.UntrustedClientIdentity, new TrustStore(),
                this.UntrustedServerIdentity, this.ServerTrustStore
            );

            (ConnectionInfo connectionToClient, ConnectionInfo connectionToServer) = connections;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(connectionToServer.Hostname, Is.EqualTo(Host));

                Assert.That(connectionToClient.Result, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
                Assert.That(connectionToClient.Reason, Is.EqualTo(RejectionReason.UnknownHostOrKey));
                Assert.That(connectionToServer.Result, Is.EqualTo(ConnectionResult.UnknownHostOrKey));
                Assert.That(connectionToServer.Reason, Is.EqualTo(RejectionReason.UnknownHostOrKey));

                Assert.That(connectionToClient.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToClient.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
                Assert.That(connectionToServer.PublicKeyFingerprint, Is.Not.Null);
                Assert.That(connectionToServer.PublicKeyFingerprint, Has.No.Length.EqualTo(0));
            }
        }
        finally
        {
            this._addressBindSemaphore.Release();
        }
    }

    private readonly struct Connections : IDisposable
    {
        public required ConnectionInfo ConnectionToServer { get; init; }
        public required ConnectionInfo ConnectionToClient { get; init; }

        public required SecureClient SecureClient
        {
            init => this._secureClient = value;
        }

        public required SecureServer SecureServer
        {
            init => this._secureServer = value;
        }

        private readonly SecureClient _secureClient;
        private readonly SecureServer _secureServer;

        public void Dispose()
        {
            this.ConnectionToServer.Connection?.Dispose();
            this.ConnectionToClient.Connection?.Dispose();
            this._secureClient.Dispose();
            this._secureServer.Dispose();
        }

        public void Deconstruct(
            out ConnectionInfo connectionToClient, out ConnectionInfo connectionToServer
        )
        {
            connectionToClient = this.ConnectionToClient;
            connectionToServer = this.ConnectionToServer;
        }
    }
}