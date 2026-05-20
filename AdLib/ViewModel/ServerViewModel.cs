using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AdLib.Config;
using AdLib.Identities;
using AdLib.Model;
using AdLib.View;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel;

public partial class ServerViewModel : PageViewModelBase
{
    private readonly FileTransferServer _server = new();
    [ObservableProperty] private string _selectedCertificate = "";
    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] private string _status = "Stopped";

    [ObservableProperty] private List<ClientInfo?> _connectedClients = [];
    [ObservableProperty] private List<string> _currentTransfers = ["No active transfer"];
    [ObservableProperty] private double _transferProgress;

    public ServerViewModel(Identity identity, string sharedFolder, char[] password)
    {
        this.Status = "Starting";
        this.SelectedPath = sharedFolder;
        this.SelectedCertificate = identity.FriendlyName;

        this._server.ClientConnected += (_, args) => this._connectedClients.Add(args.Client);
        this._server.ClientDisconnected += (_, args) => this._connectedClients.Remove(args.Client);

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

    public override Type ViewType => typeof(ServerScreen);
    public override string Title => "Hosting Server";

    [RelayCommand]
    private void PauseTransfer() { this.ChangePage(this, new ErrorViewModel("Not implemented")); }

    [RelayCommand]
    private void StopTransfer()
    {
        this._server.Stop();
        this._server.Dispose();
        this.Status = "Stopped";
        this.ChangePage(this, new StartScreenViewModel());
    }
}
