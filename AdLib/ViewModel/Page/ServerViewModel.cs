using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;

using AdLib.Config;
using AdLib.Identities;
using AdLib.Model;
using AdLib.View.Page;
using AdLib.ViewModel.Core;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel.Page;

public partial class ServerViewModel : PageViewModel
{
    public record struct ActiveTransfer
    {
        public required string ClientName;
        public required bool IsUpload;
        public string? Path;
        private string? _direction;

        public override string ToString()
        {
            this._direction ??= this.IsUpload ? "Sending" : "Receiving";

            if (this.Path is null)
            {
                return $"{this.ClientName}: idle";
            }

            return $"{this.ClientName}: {this._direction} {this.Path}";
        }
    }

    private readonly FileTransferServer _server = new();
    [ObservableProperty] private string _selectedCertificate = "";
    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] private string _status = "Stopped";

    public ObservableCollection<ClientInfo> ConnectedClients { get; } = [];
    public ObservableCollection<ActiveTransfer> CurrentTransfers { get; } = [];

    private readonly Lock _transfersLock = new();
    private readonly Lock _clientsLock = new();

    [ObservableProperty] private double _transferProgress;

    public ServerViewModel(Identity identity, string sharedFolder, char[] password)
    {
        this.Status = "Listening";
        this.SelectedPath = sharedFolder;
        this.SelectedCertificate = identity.FriendlyName;

        this._server.ClientConnected += (_, args) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (args.Client != null) this.ConnectedClients.Add(args.Client);
            });
        };

        this._server.ClientDisconnected += (_, args) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (args.Client != null)
                {
                    lock (this._clientsLock)
                    {
                        this.ConnectedClients.Remove(args.Client);
                    }

                    lock (this._transfersLock)
                    {
                        // remove any transfers associated with this client
                        List<ActiveTransfer> toRemove =
                            this.CurrentTransfers
                                .Where(t => t.ClientName == args.Client.Certificate.FriendlyName)
                                .ToList();

                        foreach (ActiveTransfer transfer in toRemove)
                        {
                            this.CurrentTransfers.Remove(transfer);
                        }
                    }
                }
            });
        };

        this._server.TransferStarting += (_, args) =>
        {
            lock (this._transfersLock)
            {
                ActiveTransfer transfer = this.CurrentTransfers.FirstOrDefault(
                    t => t.ClientName == args.Client?.Certificate.FriendlyName,
                    default(ActiveTransfer) with { ClientName = null! }
                );

                // nullable stuff is bc ClientName should never be null, but the default type has a null! value
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (transfer.ClientName is not null)
                {
                    transfer.Path = args.Path;
                }
                else
                {
                    this.CurrentTransfers.Add(new ActiveTransfer
                    {
                        ClientName = args.Client?.Certificate.FriendlyName ?? throw new InvalidOperationException(),
                        IsUpload = args.IsSending,
                        Path = args.Path,
                    });
                }
            }
        };

        this._server.TransferFinished += (_, args) =>
        {
            lock (this._transfersLock)
            {
                // int index = this.CurrentTransfers
                //                 .Index()
                //                 .Where(tuple => tuple.Item.ClientName == args.Path)
                //                 .Select(tuple => tuple.Index)
                //                 .FirstOrDefault(-1);
                IEnumerable<int> result = from tuple in this.CurrentTransfers.Index()
                                          let index = tuple.Index
                                          let item = tuple.Item
                                          where item.ClientName == args.Path
                                          select index;

                int transferIndex = result.FirstOrDefault();

                if (transferIndex != -1)
                {
                    this.CurrentTransfers.RemoveAt(transferIndex);
                }
            }
        };

        string localPath = Path.Combine(
            ConfigDirectories.ServerLocallyTrustedIdentitiesPath,
            identity.InternalName.ToString()
        );

        TrustStore globalStore = new();
        TrustStore localStore = new();
        globalStore.Load(ConfigDirectories.ServerGloballyTrustedIdentitiesPath, null);
        localStore.Load(localPath, password);

        // server, so certificates shouldn't be associated with a host
        if (localStore.TrustedHostCertificates.Any() || globalStore.TrustedHostCertificates.Any())
        {
            throw new InvalidStoreException("Server's trusted certificate store must not contain any " +
                                            "certificates that are associated with a hostname");
        }

        this._server.Start(identity, globalStore.Combine(localStore), sharedFolder);
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(ServerScreen);
    public override string Title => "Hosting Server";

    [RelayCommand]
    private void PauseTransfer() { this.ChangePage(new ErrorViewModel("Not implemented")); }

    [RelayCommand]
    private void StopTransfer()
    {
        this._server.Stop();
        this._server.Dispose();
        this.Status = "Stopped";
        this.ChangePage(new StartScreenViewModel());
    }
}
