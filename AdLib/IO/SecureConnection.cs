using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

namespace AdLib.IO;

/// <summary>
///     Encapsulates a TLS connection, including the underlying TCP client and the SSL stream.
///     This class ensures that both are disposed of correctly when the connection is closed.
/// </summary>
public sealed class SecureConnection : IDisposable
{
    private bool _disposed;

    public SecureConnection(TcpClient insecureTcpClient, SslStream sslStream)
    {
        this._insecureTcpClient = insecureTcpClient;
        this.SslStream = sslStream;
    }

    private readonly TcpClient _insecureTcpClient;
    public SslStream SslStream { get; }

    public bool HasData => this._insecureTcpClient is { Connected: true, Available: > 0 };

    public void Dispose()
    {
        if (!Interlocked.CompareExchange(ref this._disposed, true, false))
        {
            this.SslStream.Dispose();
            this._insecureTcpClient.Dispose();
        }
    }
}
