using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using AdLib.Identities;
using AdLib.IO.Messages;

namespace AdLib.IO;

public partial class MessageTransportClient
{
    public bool IsConnected { get; private set; }

    private readonly ConcurrentQueue<ClientRequest> _requests = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private TlsClient? _tlsClient = null;
    private readonly Dictionary<string, (string partPath, FileStream stream)> _activeDownloads = new();
    private readonly Random _random = new();

    public delegate void FileListingHandler(string path, FileEntry[] files);
    
    public delegate void CertificateEventHandler(string host, X509Certificate cert);
    
    public event FileListingHandler? FileListingReceived;
    public event Action<string>? GracefullyDisconnected;
    public event Action<string>? ForceDisconnected;
    public event CertificateEventHandler? UntrustedCertificateAdded;
    
    public void AddRequest(ClientRequest request)
    {
        this._requests.Enqueue(request);
    }

    /// <summary>
    /// Reads a message from the given stream. One byte is read from the stream to determine the message type,
    /// then the rest is deserialized using the appropriate struct.
    /// </summary>
    /// <param name="stream">the stream to read from</param>
    /// <returns>the deserialized message (an appropriate implementing struct of <c>IMessage</c>)</returns>
    /// <exception cref="EndOfStreamException">if the end of the stream is reached while reading</exception>
    /// <exception cref="InvalidDataException">if an unknown message ID is encountered</exception>
    private static IMessage ReadMessage(Stream stream)
    {
        // determines type
        int headerByte = stream.ReadByte();

        if (headerByte == -1)
        {
            throw new EndOfStreamException("Failed to read message header byte.");
        }

        MessageType type = (MessageType)headerByte;

        // NOTE: make sure that the switch is exhaustive
        IMessage message = type switch
        {
            MessageType.Init => new InitMessage(),
            MessageType.InitAck => new InitAckMessage(),
            MessageType.Data => new DataMessage(),
            MessageType.DataFinished => new DataFinishedMessage(),
            MessageType.FileRequest => new FileRequestMessage(),
            MessageType.StatusRequest => new StatusRequestMessage(),
            MessageType.StatusResponse => new StatusResponseMessage(),
            MessageType.MakeDir => new MakeDirMessage(),
            MessageType.ListFiles => new ListFilesMessage(),
            MessageType.ListFilesResponse => new ListFilesResponseMessage(),
            MessageType.Delete => new DeleteMessage(),
            MessageType.HashCheck => new HashCheckMessage(),
            MessageType.HashResponse => new HashResponseMessage(),
            MessageType.ControlAck => new ControlAckMessage(),
            MessageType.End => new EndMessage(),
            MessageType.EndAck => new EndAckMessage(),
            MessageType.ErrorRecoverable => new ErrorRecoverableMessage(),
            MessageType.ErrorFatal => new ErrorFatalMessage(),
            _ => throw new InvalidDataException($"Unknown message header byte: 0x{headerByte:X2}")
        };

        // this initializes all of the fields on the struct
        message.Deserialize(stream);
        return message;
    }

    public void ConnectAndListen(string host, Identity identity)
    {
        Thread thread = new(() =>
        {
            try
            {
                this._tlsClient = new TlsClient();
                ThreadLoop();
            }
            finally
            {
                this._tlsClient?.Dispose();
            }
        });
        thread.Start();
        return;

        void ThreadLoop()
        {
            ConnectionResult result = this._tlsClient.Connect(host, identity);

            if (this._tlsClient.SslStream is null)
            {
                throw new InvalidOperationException("TLS stream is null after creation");
            }

            if (result == ConnectionResult.UntrustedCertificate)
            {
                if (this._tlsClient.SslStream.RemoteCertificate is null)
                {
                    throw new IOException("Remote host did not offer a certificate");
                }

                this.UntrustedCertificateAdded?.Invoke(host, this._tlsClient.SslStream.RemoteCertificate);

                return;
            }

            if (result != ConnectionResult.Success)
            {
                IOException ex = new($"Connection failed: {result}");
                this.ForceDisconnected?.Invoke($"{ex.GetType()}: {ex.Message}");
                throw ex;
            }

            this.IsConnected = true;
            // do handshake
            this.SendMessage(new InitMessage());
            InitAckMessage acknowledgement = new();

            int type = this._tlsClient.SslStream.ReadByte();

            if (type == -1)
            {
                throw new IOException("Stream closed before initialization was completed");
            }

            if ((MessageType)type != MessageType.InitAck)
            {
                throw new IOException("Invalid initialization response");
            }

            acknowledgement.Deserialize(this._tlsClient.SslStream);

            // comms loop
            while (!this._cancellationTokenSource.Token.IsCancellationRequested)
            {
                // self-cancel on disconnect
                if (!this.IsConnected)
                {
                    this._cancellationTokenSource.Cancel();
                }

                this.CommunicateLoop(this._tlsClient);
                Thread.Sleep(10); // avoid busy loop
            }

            // cancelled - clean up
        }
    }

    private void CommunicateLoop(TlsClient tlsClient)
    {
        if (tlsClient.SslStream == null) return;

        try
        {
            // client-side requests
            while (this._requests.TryDequeue(out ClientRequest request))
            {
                this.HandleRequest(request);
            }

            // server messages
            while (tlsClient.HasData)
            {
                IMessage message = ReadMessage(tlsClient.SslStream);
                this.HandleServerMessage(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error occurred while connected to remote host: " +
                              $"{ex.GetType().Name}: {ex.Message}");
            this.IsConnected = false;
        }
    }

    private void SendMessage(IMessage message)
    {
        if (this._tlsClient?.SslStream == null) return;
        try
        {
            this._tlsClient.SslStream.WriteByte((byte)message.Header);
            message.Serialize(this._tlsClient.SslStream);
            this._tlsClient.SslStream.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send message {message.Header}: {ex.Message}");
            this.IsConnected = false;
        }
    }

    private void ExpectAcknowledgement(MessageType type)
    {
        if (this._tlsClient?.SslStream == null)
        {
            throw new InvalidOperationException("cannot expect when disconnected");
        }

        ControlAckMessage message = new();
        message.Deserialize(this._tlsClient.SslStream);
        if (message.ControlCode != (byte)type)
        {
            throw new InvalidOperationException($"Expected {type} acknowledgement, got {message.ControlCode}");
        }
    }
    
    private void HandleRequest(ClientRequest request)
    {
        Console.WriteLine($"Processing request: {request.Type} for {request.Path}");
        switch (request.Type)
        {
            case ClientRequestType.PutPath:
                this.UploadPath(request.Path, Path.GetFileName(request.Path));
                break;
            case ClientRequestType.GetPath:
                this.SendMessage(new FileRequestMessage { Path = request.Path });
                break;
            case ClientRequestType.DeleteRemotePath:
                this.SendMessage(new DeleteMessage { Path = request.Path });
                this.ExpectAcknowledgement(MessageType.Delete);
                break;
            case ClientRequestType.MakeRemoteDir:
                this.SendMessage(new MakeDirMessage { Path = request.Path });
                this.ExpectAcknowledgement(MessageType.MakeDir);
                break;
            case ClientRequestType.ListFiles:
                this.SendMessage(new ListFilesMessage { Path = request.Path });
                break;
            case ClientRequestType.Disconnect:
                this.SendMessage(new EndMessage());
                break;
        }
    }

    private void HandleServerMessage(IMessage message)
    {
        switch (message)
        {
            case InitAckMessage:
                Console.WriteLine("Handshake completed.");
                break;
            case ErrorFatalMessage fatal:
                Console.WriteLine($"Fatal server error: {fatal.Errno}");
                this.IsConnected = false;
                break;
            case ErrorRecoverableMessage recoverable:
                Console.WriteLine($"Recoverable server error: {recoverable.Errno}");
                break;
            case EndMessage:
                this.SendMessage(new EndAckMessage());
                this.IsConnected = false;
                break;
            case EndAckMessage:
                Console.WriteLine("Disconnected by server (EndAck).");
                this.IsConnected = false;
                break;
            case ListFilesResponseMessage listFiles:
                Console.WriteLine($"Received file list for {listFiles.Files.Length} items.");
                this.FileListingReceived?.Invoke(listFiles.Path, listFiles.Files);
                break;
            case DataMessage data:
                this.HandleIncomingData(data);
                break;
            case DataFinishedMessage finished:
                this.FinalizeDownload(finished.Path);
                break;
            case MakeDirMessage makeDir:
                Directory.CreateDirectory(makeDir.Path);
                break;
            case ControlAckMessage ack:
                Console.WriteLine($"Received ControlAck: {ack.ControlCode}");
                break;
            default:
                Console.WriteLine($"Received message: {message.Header}");
                break;
        }
    }

    private void HandleIncomingData(DataMessage data)
    {
        if (!this._activeDownloads.TryGetValue(data.Path, out var download))
        {
            string partPath = $"{data.Path}.{this._random.Next():x8}.part";
            string? directory = Path.GetDirectoryName(data.Path);
            if (directory != null) Directory.CreateDirectory(directory);
            
            FileStream stream = File.OpenWrite(partPath);
            download = (partPath, stream);
            this._activeDownloads[data.Path] = download;
        }

        download.stream.Write(data.Data);
    }

    private void FinalizeDownload(string path)
    {
        if (this._activeDownloads.Remove(path, out var download))
        {
            download.stream.Dispose();
            if (File.Exists(path)) File.Delete(path);
            File.Move(download.partPath, path);
            Console.WriteLine($"Downloaded: {path}");
        }
    }

    private void UploadPath(string localPath, string remotePath)
    {
        if (File.Exists(localPath))
        {
            try
            {
                // upload file in chunks to avoid massive buffering requirements
                long totalDataSize = new FileInfo(localPath).Length;
                long currentOffset = 0;
                using FileStream fileStream = File.OpenRead(localPath);

                byte[] data = new byte[FILE_BUFFER_SIZE];
                while (currentOffset < totalDataSize)
                {
                    currentOffset += fileStream.Read(data);

                    this.SendMessage(new DataMessage
                    {
                        Path = remotePath,
                        Data = data,
                        Crc32 = Crc32.HashToUInt32(data),
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to upload file {localPath}: {ex.Message}");
            }
        }
        else if (Directory.Exists(localPath))
        {
            this.SendMessage(new MakeDirMessage { Path = remotePath });
            foreach (string file in Directory.GetFiles(localPath))
            {
                // plain upload
                this.UploadPath(file, $"{remotePath}/{Path.GetFileName(file)}");
            }
            foreach (string dir in Directory.GetDirectories(localPath))
            {
                // recurses
                this.UploadPath(dir, Path.Combine(remotePath, Path.GetFileName(dir)));
            }
        }
        else
        {
            Console.WriteLine($"Cannot find file or folder {localPath}");
        }
    }

    private const int FILE_BUFFER_SIZE = 32768; // not a huge allocation burden
}
