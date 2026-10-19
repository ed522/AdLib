using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using AdLib.IO.Messages;
using AdLib.Utils;

namespace AdLib.IO;

public interface IMessageChannel : IDisposable, IAsyncDisposable
{
    public event EventHandler<EventArgs<IMessage>> MessageReceived;

    public IMessage? ReadMessageIfAvailable();
    public IMessage? ReadMessageIfAvailable(int timeout);
    public IMessage ReadMessage();
    public Task<IMessage> ReadMessageAsync(CancellationToken ct = default);
    public bool TryReadMessageIfAvailable([NotNullWhen(true)] out IMessage? message);
    public bool HasMessage();

    public void WriteMessage(IMessage message);
    public Task WriteMessageAsync(IMessage message, CancellationToken ct = default);
}