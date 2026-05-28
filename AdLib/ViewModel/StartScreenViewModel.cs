using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

using AdLib.Config;
using AdLib.Identities;
using AdLib.View;
using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using static AdLib.Identities.IdentityStore;

namespace AdLib.ViewModel;

public partial class StartScreenViewModel : PageViewModel
{
    [ObservableProperty] private IdentityLabel _clientSelectedIdentity;
    [ObservableProperty] private IdentityLabel _serverSelectedIdentity;

    public StartScreenViewModel() => this.RefreshIdentities();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(StartScreen);

    public override string Title => "Welcome";

    public string ClientTargetIp { get; set; } = "";
    public string ServerSharedFolder { get; set; } = "";

    private readonly IdentityStore _clientStore = new(ConfigDirectories.ClientOwnedIdentitiesPath, true);
    private readonly IdentityStore _serverStore = new(ConfigDirectories.ServerOwnedIdentitiesPath, false);

    private void RefreshIdentities()
    {
        this.ClientAvailableIdentities.Clear();
        this.ServerAvailableIdentities.Clear();

        foreach (IdentityLabel label in this._clientStore.AvailableIdentities)
        {
            this.ClientAvailableIdentities.Add(label);
        }

        foreach (IdentityLabel label in this._serverStore.AvailableIdentities)
        {
            this.ServerAvailableIdentities.Add(label);
        }
    }

    public ObservableCollection<IdentityLabel> ServerAvailableIdentities { get; } = [];
    public ObservableCollection<IdentityLabel> ClientAvailableIdentities { get; } = [];

    [RelayCommand]
    public async Task CreateNewServerIdentity()
    {
        if (await this.OpenModalAsync(new IdentityCreationModalViewModel()) is not
            IdentityCreationModalViewModel modal)
        {
            throw new InvalidOperationException("Invalid modal received");
        }

        string friendlyName = modal.Name;
        char[] password = modal.Password.ToCharArray();

        Identity ident = this._serverStore.CreateNewIdentity(friendlyName, password);
        this.ServerAvailableIdentities.Add(new IdentityLabel(ident.InternalName, ident.FriendlyName));
    }

    [RelayCommand]
    public async Task CreateNewClientIdentity()
    {
        if (await this.OpenModalAsync(new IdentityCreationModalViewModel()) is not
            IdentityCreationModalViewModel modal)
        {
            throw new InvalidOperationException("Invalid modal received");
        }

        string friendlyName = modal.Name;
        char[] password = modal.Password.ToCharArray();

        Identity ident = this._clientStore.CreateNewIdentity(friendlyName, password);
        IdentityLabel label = new(ident.InternalName, ident.FriendlyName);
        this.ClientAvailableIdentities.Add(label);
        this.ClientSelectedIdentity = label;
    }

    [RelayCommand]
    public void SelectClientIdentityCommand(IdentityLabel label) => this.ClientSelectedIdentity = label;

    [RelayCommand]
    public void SelectServerIdentityCommand(IdentityLabel label) => this.ServerSelectedIdentity = label;

    [RelayCommand]
    public void GoToClientScreen() =>
        this.ChangePage(new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToServerScreen()
    {
        // load identity
        // TODO get user's password (modal?)
        char[] password = [];

        Identity identity = Identity.LoadFromFile(
            ConfigDirectories.ServerOwnedIdentitiesPath,
            this.ServerSelectedIdentity.InternalName,
            password
        );

        this.ChangePage(new ServerViewModel(identity, this.ServerSharedFolder, password));
    }

    [RelayCommand]
    public void GoToAboutScreen() =>
        this.ChangePage(new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToSettingsScreen() =>
        this.ChangePage(new ErrorViewModel("Not implemented"));
}
