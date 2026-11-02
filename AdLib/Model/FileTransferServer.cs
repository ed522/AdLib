using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using AdLib.Identities;
using AdLib.IO;

namespace AdLib.Model;

public sealed partial class FileTransferServer : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<ClientHandler> _clients = [];
    private readonly Lock _clientsLock = new();
    private string _rootPath = "";
    private SecureServer? _tlsServer;

    public void Dispose()
    {
        this.Stop();
        this._tlsServer?.Dispose();
    }

    /// <summary>
    ///     Called when a client connects to the server.
    /// </summary>
    public event EventHandler<ConnectedEventArgs>? ClientConnected;

    /// <summary>
    ///     Called when a client disconnects from the server.
    /// </summary>
    public event EventHandler<DisconnectedEventArgs>? ClientDisconnected;

    /// <summary>
    ///     Called when a fatal error occurs in any client handler. If the error was caused by an exception,
    ///     the <c>Exception</c> object is provided. The handler will be forcefully disconnected and disposed
    ///     of after any fatal error. If the ClientInfo parameter is <c>null</c>, the error occurred in the 
    ///     server base itself.
    /// </summary>
    public event EventHandler<FatalErrorOccurredEventArgs>? FatalErrorOccurred;

    /// <summary>
    ///     Called when a recoverable (non-fatal) error occurs in any client handler. If the error was caused
    ///     by an exception, the <c>Exception</c> object is provided. The handler will remain connected. 
    /// </summary>
    public event EventHandler<RecoverableErrorOccurredEventArgs>? RecoverableErrorOccurred;

    /// <summary>
    ///     Called when a file transfer starts (either upload or download).
    /// </summary>
    public event EventHandler<TransferStartingEventArgs>? TransferStarting;

    /// <summary>
    ///     Called when a file is successfully transferred to or from a client.
    /// </summary>
    public event EventHandler<TransferFinishedEventArgs>? TransferFinished;

    /// <summary>
    ///     Called when a client fails to authenticate for some reason. The certificate might be null if
    ///     the client fails to present one, in which case it is not possible to trust nor communicate with
    ///     them.
    /// </summary>
    public event SecureConnectionUtils.AuthenticationErrorHandler? AuthenticationError;

    private async Task ListenForNetworkClients(
        ChannelWriter<ClientInfo> clientInfoWriter, Identity identity, TrustStore store,
        CancellationToken token = default
    )
    {
        while (!token.IsCancellationRequested)
        {
            this._tlsServer = new SecureServer(identity, store);
            this._tlsServer.Start();

            // threaded so async is useless
            var connectionInfo = await this._tlsServer.AcceptClientAsync(token);

            // client disposes connection in its Dispose, and that's called on thread exit
            var connection = connectionInfo.Connection;

            string host = connectionInfo.Hostname;
            var result = connectionInfo.Result;
            var reason = connectionInfo.Reason;
            var publicKey = connectionInfo.PublicKey;
            PublicKeyInfo? publicKeyInfo;

            if (publicKey is not null)
            {
                publicKeyInfo = store.FindPublicKeyOrDefault(publicKey);
            }
            else
            {
                publicKeyInfo = null;
            }

            // did the authentication fail?
            // ConnectionResult holds the local auth result (against remote host), RejectionReason is
            // for remote auth (remote host checking our cert)

            if (result != SecureConnectionUtils.ConnectionResult.Success ||
                reason != SecureConnectionUtils.RejectionReason.None)
            {
                // subscribers will handle both remote rejections (reason) and
                // local rejections (result)
                this.AuthenticationError?.Invoke(host, publicKeyInfo, publicKey, result, reason);
                continue;
            }

            if (connection is null)
            {
                continue; // failed to connect
            }

            if (publicKeyInfo is null)
            {
                clientInfoWriter.TryComplete();

                throw new InvalidOperationException(
                    "Connection returned a null public key even though authentication succeeded - this is a bug");
            }

            // should be value-unique (but since this is a reference type, even if the same client
            // connects twice with the same identity for some reason, it will still be not equal)
            ClientInfo clientInfo = new()
            {
                RemoteEndPoint = host,
                PublicKeyInfo = publicKeyInfo,
                MessageChannel = CreateMessageChannel(connection),
            };

            await clientInfoWriter.WriteAsync(clientInfo, token);
        }
    }

    private static BlockMessageChannel CreateMessageChannel(SecureConnection connection) =>
        new(new SshDataBlockChannel(connection.Channel));

    public async Task Start(
        Identity identity, TrustStore store, string sharedPath, ChannelReader<ClientInfo>? clientInfoSupplier = null,
        CancellationToken token = default
    )
    {
        if (token != CancellationToken.None)
        {
            token = CancellationTokenSource.CreateLinkedTokenSource(this._cancellationTokenSource.Token, token).Token;
        }
        else
        {
            token = this._cancellationTokenSource.Token;
        }

        var networkListener = Task.CompletedTask;
        CancellationTokenSource? networkListenerCancellation = null;

        if (clientInfoSupplier is null)
        {
            var channel = Channel.CreateUnbounded<ClientInfo>();
            clientInfoSupplier = channel.Reader;
            networkListenerCancellation = new CancellationTokenSource();
            token.Register(networkListenerCancellation.Cancel);

            networkListener
                = this.ListenForNetworkClients(channel.Writer, identity, store, networkListenerCancellation.Token);
        }

        this._rootPath = Path.GetFullPath(sharedPath);

        if (!Directory.Exists(this._rootPath))
        {
            throw new InvalidOperationException($"Path {this._rootPath} does not exist");
        }

        // server setup

        // define + start thread - since we hold a cancellation token we don't need to worry about keeping
        // references to stop it
        // ClientHandler is threaded so this can handle multiple at once
        var listenerTask = Task.Run(async () =>
        {
            // keep accepting clients until the server is stopped
            while (!token.IsCancellationRequested && await clientInfoSupplier.WaitToReadAsync(token))
            {
                try
                {
                    var clientInfo = await clientInfoSupplier.ReadAsync(token);
                    ClientHandler handler = new(clientInfo, this._rootPath, token);

                    // forward all events to our subscribers (but give them the client info)

                    // get rid of client on disconnect
                    handler.Disconnected += (sender, args) =>
                    {
                        if (sender is ClientHandler toRemove)
                        {
                            lock (this._clientsLock)
                            {
                                this._clients.Remove(toRemove);
                            }

                            args.Client = toRemove.Info;
                            this.ClientDisconnected?.Invoke(this, args);
                        }
                    };

                    handler.FatalErrorOccurred += (sender, args) =>
                    {
                        // disconnect logic takes care of removing the handler
                        if (sender is ClientHandler currentHandler)
                        {
                            args.Client = currentHandler.Info;
                            this.FatalErrorOccurred?.Invoke(this, args);
                        }
                    };

                    handler.RecoverableErrorOccurred += (sender, args) =>
                    {
                        if (sender is ClientHandler currentHandler)
                        {
                            args.Client = currentHandler.Info;
                            this.RecoverableErrorOccurred?.Invoke(currentHandler.Info, args);
                        }
                    };

                    handler.TransferStarting += (sender, args) =>
                    {
                        if (sender is ClientHandler currentHandler)
                        {
                            args.Client = currentHandler.Info;
                            this.TransferStarting?.Invoke(this, args);
                        }
                    };

                    handler.TransferFinished += (sender, args) =>
                    {
                        if (sender is ClientHandler currentHandler)
                        {
                            args.Client = currentHandler.Info;
                            this.TransferFinished?.Invoke(currentHandler.Info, args);
                        }
                    };

                    lock (this._clientsLock)
                    {
                        this._clients.Add(handler);
                    }

                    this.ClientConnected?.Invoke(this, new ConnectedEventArgs { Client = clientInfo });
                    // client's partly connected so let the handler do its thing
                    await handler.Start();
                }
                catch (Exception ex) when (ex is OperationCanceledException)
                {
                    // kill thread - this means the entire server was killed
                    return;
                }
                finally
                {
                    networkListenerCancellation?.Cancel();
                }
            }

            networkListenerCancellation?.Cancel();
        }, token);

        await Task.WhenAll(listenerTask, networkListener);
    }

    public void Stop()
    {
        this._cancellationTokenSource.Cancel();
        this._tlsServer?.Stop();

        lock (this._clientsLock)
        {
            foreach (var client in this._clients)
            {
                client.Dispose();
            }

            this._clients.Clear();
        }
    }
}