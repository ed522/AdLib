using System;
using System.Net.Sockets;
using System.Threading;

using Microsoft.DevTunnels.Ssh;

namespace AdLib.IO;

/// <summary>
///     Encapsulates a TLS connection, including the underlying TCP client and the SSL stream.
///     This class ensures that both are disposed of correctly when the connection is closed.
/// </summary>
public sealed class SecureConnection : IDisposable
{
    private bool _disposed;

    public SecureConnection(TcpClient insecureClient, SshChannel channel)
    {
        this._insecureClient = insecureClient;
        this.Channel = channel;
    }

    private readonly TcpClient _insecureClient;
    public SshChannel Channel { get; }

    public void Dispose()
    {
        if (!Interlocked.CompareExchange(ref this._disposed, true, false))
        {
            this.Channel.Dispose();
            this.Channel.Session.Dispose();
            this._insecureClient.Dispose();
        }
    }
}
