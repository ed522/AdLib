using System.Threading.Channels;

using AdLib.IO;
using AdLib.IO.Messages;

using Moq;

namespace AdLib.Tests.Communications;

[TestFixture]
public class BlockMessageChannelTests
{
    private static IEnumerable<TestCaseData> MessageCases()
    {
        yield return Case("Init", new InitMessage { PathSeparator = '/' });
        yield return Case("InitAck", new InitAckMessage { SharedFolderPath = "/shared/folder", PathSeparator = '/' });

        yield return Case("Data", new DataMessage
        {
            Path = "file.bin",
            Data = [0x00, 0x01, 0x02, 0xFF],
            Offset = 1234567890123,
            Crc32 = 0xDEADBEEF,
        });

        yield return Case("DataFinished", new DataFinishedMessage { Path = "file.bin" });

        yield return Case("FileRequest", new FileRequestMessage
        {
            RemotePath = "/remote/file.bin",
            DestinationPath = "/local/file.bin",
        });

        yield return Case("ResendRequest", new ResendRequestMessage { Path = "file.bin", Offset = 9876543210 });
        yield return Case("StatusRequest", new StatusRequestMessage { Random = 0x12345678 });
        yield return Case("StatusResponse", new StatusResponseMessage { Random = 0x87654321 });
        yield return Case("MakeDir", new MakeDirMessage { Path = "/new/directory" });
        yield return Case("ListFiles", new ListFilesMessage { Path = "/some/directory" });

        yield return Case("ListFilesResponse", new ListFilesResponseMessage
        {
            Path = "/some/directory",
            Files =
            [
                new FileEntry { Name = "a.txt", IsDirectory = false },
                new FileEntry { Name = "sub", IsDirectory = true },
                new FileEntry { Name = "empty", IsDirectory = false },
            ],
        });

        yield return Case("Delete", new DeleteMessage { Path = "/file/to/delete" });

        yield return Case("HashCheck", new HashCheckMessage
        {
            Path = "file.bin",
            ExpectedHash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
        });

        yield return Case("HashResponseValid", new HashResponseMessage { Status = true });
        yield return Case("HashResponseInvalid", new HashResponseMessage { Status = false });
        yield return Case("ControlAck", new ControlAckMessage { ControlCode = 0x42 });
        yield return Case("End", new EndMessage());
        yield return Case("EndAck", new EndAckMessage());
        yield return Case("ErrorRecoverable", new ErrorRecoverableMessage { Errno = RecoverableError.PathOutOfScope });
        yield return Case("ErrorFatal", new ErrorFatalMessage { Errno = FatalError.InvalidMessage });
    }

    private static TestCaseData Case(string name, IMessage message) =>
        new(message) { TestName = name };

    private static Mock<IDataBlockChannel> CreateChannel(ChannelReader<byte[]> incomingBlocks)
    {
        Mock<IDataBlockChannel> channel = new(MockBehavior.Strict);

        channel.Setup(c => c.GetBlockImmediately()).Returns(() =>
        {
            incomingBlocks.TryRead(out byte[]? block);
            return block;
        });

        channel.Setup(c => c.GetBlock()).Returns(() =>
        {
            byte[]? block;
            while (!incomingBlocks.TryRead(out block)) ;
            return block;
        });

        channel.Setup(c => c.GetBlock(It.IsAny<int>())).Returns((int i) =>
        {
            CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(i));
            byte[]? block = null;
            while (!cts.Token.IsCancellationRequested && !incomingBlocks.TryRead(out block)) ;
            return block;
        });

        channel.Setup(c => c.GetBlockAsync(It.IsAny<CancellationToken>()))
               .Returns((CancellationToken c) => incomingBlocks.ReadAsync(c).AsTask());

        channel.Setup(c => c.PutBlock(It.IsAny<byte[]>()));
        channel.Setup(c => c.Dispose());
        channel.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        channel.Setup(c => c.HasBlock()).Returns(() => incomingBlocks.TryPeek(out _));
        return channel;
    }

    private static Mock<IDataBlockChannel> CreateChannel(byte[]? block = null)
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        if (block is not null) channel.Writer.TryWrite(block);
        return CreateChannel(channel.Reader);
    }

    private static byte[] Serialize(IMessage message)
    {
        var body = message.Serialize();
        byte[] result = new byte[body.Length + 1];
        result[0] = (byte)message.Header;
        body.CopyTo(result.AsSpan()[1..]);
        return result;
    }

    private static void AssertMessagesEqual(IMessage expected, IMessage actual)
    {
        Assert.That(actual, Is.TypeOf(expected.GetType()));
        Assert.That(actual.Header, Is.EqualTo(expected.Header));

        switch (expected)
        {
            case InitMessage e:
                Assert.That(((InitMessage)actual).PathSeparator, Is.EqualTo(e.PathSeparator));
                break;

            case InitAckMessage e:
                Assert.That(((InitAckMessage)actual).SharedFolderPath, Is.EqualTo(e.SharedFolderPath));
                Assert.That(((InitAckMessage)actual).PathSeparator, Is.EqualTo(e.PathSeparator));
                break;

            case DataMessage e:
                var a = (DataMessage)actual;

                Assert.Multiple(() =>
                {
                    Assert.That(a.Path, Is.EqualTo(e.Path));
                    Assert.That(a.Data, Is.EqualTo(e.Data));
                    Assert.That(a.Offset, Is.EqualTo(e.Offset));
                    Assert.That(a.Crc32, Is.EqualTo(e.Crc32));
                });

                break;

            case DataFinishedMessage e:
                Assert.That(((DataFinishedMessage)actual).Path, Is.EqualTo(e.Path));
                break;

            case FileRequestMessage e:
                var fr = (FileRequestMessage)actual;

                Assert.Multiple(() =>
                {
                    Assert.That(fr.RemotePath, Is.EqualTo(e.RemotePath));
                    Assert.That(fr.DestinationPath, Is.EqualTo(e.DestinationPath));
                });

                break;

            case ResendRequestMessage e:
                var rr = (ResendRequestMessage)actual;

                Assert.Multiple(() =>
                {
                    Assert.That(rr.Path, Is.EqualTo(e.Path));
                    Assert.That(rr.Offset, Is.EqualTo(e.Offset));
                });

                break;

            case StatusRequestMessage e:
                Assert.That(((StatusRequestMessage)actual).Random, Is.EqualTo(e.Random));
                break;

            case StatusResponseMessage e:
                Assert.That(((StatusResponseMessage)actual).Random, Is.EqualTo(e.Random));
                break;

            case MakeDirMessage e:
                Assert.That(((MakeDirMessage)actual).Path, Is.EqualTo(e.Path));
                break;

            case ListFilesMessage e:
                Assert.That(((ListFilesMessage)actual).Path, Is.EqualTo(e.Path));
                break;

            case ListFilesResponseMessage e:
                var lf = (ListFilesResponseMessage)actual;
                Assert.That(lf.Path, Is.EqualTo(e.Path));
                Assert.That(lf.Files, Has.Length.EqualTo(e.Files.Length));

                for (int i = 0; i < e.Files.Length; i++)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(lf.Files[i].Name, Is.EqualTo(e.Files[i].Name));
                        Assert.That(lf.Files[i].IsDirectory, Is.EqualTo(e.Files[i].IsDirectory));
                    });
                }

                break;

            case DeleteMessage e:
                Assert.That(((DeleteMessage)actual).Path, Is.EqualTo(e.Path));
                break;

            case HashCheckMessage e:
                var hc = (HashCheckMessage)actual;

                Assert.Multiple(() =>
                {
                    Assert.That(hc.Path, Is.EqualTo(e.Path));
                    Assert.That(hc.ExpectedHash, Is.EqualTo(e.ExpectedHash));
                });

                break;

            case HashResponseMessage e:
                Assert.That(((HashResponseMessage)actual).Status, Is.EqualTo(e.Status));
                break;

            case ControlAckMessage e:
                Assert.That(((ControlAckMessage)actual).ControlCode, Is.EqualTo(e.ControlCode));
                break;

            case EndMessage:
            case EndAckMessage:
                break;

            case ErrorRecoverableMessage e:
                Assert.That(((ErrorRecoverableMessage)actual).Errno, Is.EqualTo(e.Errno));
                break;

            case ErrorFatalMessage e:
                Assert.That(((ErrorFatalMessage)actual).Errno, Is.EqualTo(e.Errno));
                break;

            default:
                Assert.Fail($"Unhandled message type {expected.GetType().Name}");
                break;
        }
    }

    // TEST behaviour of each method

    [TestCaseSource(nameof(MessageCases))]
    public void ReadMessage_DeserializesAllMessageTypes(IMessage expected)
    {
        var channel = CreateChannel(Serialize(expected));
        using BlockMessageChannel messageChannel = new(channel.Object);

        var actual = messageChannel.ReadMessage();

        AssertMessagesEqual(expected, actual);
    }

    [TestCaseSource(nameof(MessageCases))]
    public void ReadMessageIfAvailable_DeserializesAllMessageTypes(IMessage expected)
    {
        var channel = CreateChannel(Serialize(expected));
        using BlockMessageChannel messageChannel = new(channel.Object);

        var actual = messageChannel.ReadMessageIfAvailable();

        Assert.That(actual, Is.Not.Null);
        AssertMessagesEqual(expected, actual!);
    }

    [TestCaseSource(nameof(MessageCases))]
    public void WriteMessage_SerializesAllMessageTypes(IMessage message)
    {
        var channel = CreateChannel();
        using BlockMessageChannel messageChannel = new(channel.Object);

        messageChannel.WriteMessage(message);

        byte[] expected = Serialize(message);
        channel.Verify(c => c.PutBlock(It.Is<byte[]>(b => ((IEnumerable<byte>)b).SequenceEqual(expected))), Times.Once);
    }

    [TestCaseSource(nameof(MessageCases))]
    public void RoundTrip_PreservesAllMessageTypes(IMessage expected)
    {
        byte[]? written = null;
        var channel = CreateChannel();

        channel.Setup(c => c.PutBlock(It.IsAny<byte[]>()))
               .Callback<byte[]>(b => written = b);

        channel.Setup(c => c.GetBlockImmediately()).Returns(() => written);

        using BlockMessageChannel messageChannel = new(channel.Object);

        messageChannel.WriteMessage(expected);
        var actual = messageChannel.ReadMessageIfAvailable();

        Assert.That(actual, Is.Not.Null);
        AssertMessagesEqual(expected, actual!);
    }

    [Test, CancelAfter(1000)]
    public void ReadMessageIfAvailable_ReturnsNull_WhenNoMessageAvailable(CancellationToken ct)
    {
        ct.Register(() => Assert.Fail("ReadMessageIfAvailable should not block forever - timed out"));
        var channel = CreateChannel();
        using BlockMessageChannel messageChannel = new(channel.Object);
        Assert.That(messageChannel.ReadMessageIfAvailable(), Is.Null);
    }

    [Test]
    public void ReadMessageIfAvailable_ReturnsNull_AfterTimeout()
    {
        var channel = CreateChannel();
        using BlockMessageChannel messageChannel = new(channel.Object);
        Assert.That(messageChannel.ReadMessageIfAvailable(100), Is.Null);
    }

    [Test, MaxTime(1000), CancelAfter(1000)]
    public void ReadMessageIfAvailable_ReturnsMessage_AfterDelayedMessageBeforeTimeout(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        var mock = CreateChannel(channel.Reader);

        // does not need await
        _ = Task.Run(async () =>
        {
            await Task.Delay(100, ct);
            channel.Writer.TryWrite(Serialize(new InitMessage { PathSeparator = '/' }));
        }, ct);

        using BlockMessageChannel messageChannel = new(mock.Object);
        Assert.That(messageChannel.ReadMessageIfAvailable(1000), Is.Not.Null);
    }

    [Test, MaxTime(600), CancelAfter(600)]
    public void ReadMessageIfAvailable_ReturnsNull_WithLateMessageAfterTimeout(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        var mock = CreateChannel(channel.Reader);

        // does not need await
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, ct);
            channel.Writer.TryWrite(Serialize(new InitMessage { PathSeparator = '/' }));
        }, ct);

        using BlockMessageChannel messageChannel = new(mock.Object);
        Assert.That(messageChannel.ReadMessageIfAvailable(100), Is.Null);
    }

    [Test]
    public void ReadMessage_Blocks_WhenNoMessageAvailable()
    {
        bool waitOver = false;
        var channel = Channel.CreateUnbounded<byte[]>();
        var mock = CreateChannel(channel.Reader);
        using BlockMessageChannel messageChannel = new(mock.Object);

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            waitOver = true;
            channel.Writer.TryWrite(Serialize(new InitMessage { PathSeparator = '/' }));
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(messageChannel.ReadMessage(), Is.Not.Null);
            Assert.That(waitOver, Is.True);
        }
    }

    [Test]
    public void ReadMessage_IsNotNull_WhenMessageAvailable()
    {
        var mock = CreateChannel(Serialize(new InitMessage { PathSeparator = '/' }));
        using BlockMessageChannel messageChannel = new(mock.Object);

        Assert.That(messageChannel.ReadMessage(), Is.Not.Null);
    }

    [Test]
    public async Task ReadMessageAsync_ReturnsMessage_WhenMessageAvailable()
    {
        var channel = Channel.CreateUnbounded<byte[]>();
        var mock = CreateChannel(channel.Reader);
        await using BlockMessageChannel messageChannel = new(mock.Object);

        channel.Writer.TryWrite(Serialize(new InitMessage { PathSeparator = '/' }));

        Assert.That(await messageChannel.ReadMessageAsync(), Is.Not.Null);
    }

    [Test]
    public async Task ReadMessageAsync_OnlyStallsTask_WhenNoMessageAvailable()
    {
        ManualResetEventSlim signal = new(false);
        var channel = Channel.CreateUnbounded<byte[]>();
        var mock = CreateChannel(channel.Reader);
        await using BlockMessageChannel messageChannel = new(mock.Object);

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            channel.Writer.TryWrite(Serialize(new InitMessage { PathSeparator = '/' }));
            signal.Set();
        });

        var task = messageChannel.ReadMessageAsync();

        // ASSUMPTION: the following Assert runs in <<1000ms
        using (Assert.EnterMultipleScope())
        {
            Assert.That(signal.IsSet, Is.False);
            Assert.That(await task, Is.Not.Null);
            Assert.That(signal.IsSet, Is.True);
        }
    }

    [Test]
    public void HasMessage_IsFalse_WhenNoMessageAvailable()
    {
        var mock = CreateChannel(Channel.CreateUnbounded<byte[]>());
        using BlockMessageChannel messageChannel = new(mock.Object);

        Assert.That(messageChannel.HasMessage(), Is.False);
    }

    [Test]
    public void HasMessage_IsTrue_WhenMessageAvailable()
    {
        var mock = CreateChannel(Serialize(new InitMessage { PathSeparator = '/' }));
        using BlockMessageChannel messageChannel = new(mock.Object);

        Assert.That(messageChannel.HasMessage(), Is.True);
    }

    [Test]
    public void TryReadMessageIfAvailable_ReturnsFalseWithNullOut_WhenNoMessageAvailable()
    {
        var mock = CreateChannel(Channel.CreateUnbounded<byte[]>());
        using BlockMessageChannel messageChannel = new(mock.Object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(messageChannel.TryReadMessageIfAvailable(out var result), Is.False);
            Assert.That(result, Is.Null);
        }
    }

    [Test]
    public void TryReadMessageIfAvailable_ReturnsTrueWithOut_WhenMessageAvailable()
    {
        var mock = CreateChannel(Serialize(new InitMessage { PathSeparator = '/' }));
        using BlockMessageChannel messageChannel = new(mock.Object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(messageChannel.TryReadMessageIfAvailable(out var result), Is.True);
            Assert.That(result, Is.Not.Null);
        }
    }
}