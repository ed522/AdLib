using System;
using System.Collections.ObjectModel;

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
    public override Type ViewType => typeof(StartScreen);
    public override string Title => "Welcome";

    public string ClientTargetIp { get; set; } = "";
    public string ServerSharedFolder { get; set; } = "";

    public ObservableCollection<IdentityLabel> ServerAvailableIdentities { get; } = [];
    public ObservableCollection<IdentityLabel> ClientAvailableIdentities { get; } = [];

    [ObservableProperty] private IdentityLabel _serverSelectedIdentity;
    [ObservableProperty] private IdentityLabel _clientSelectedIdentity;

    public StartScreenViewModel()
    {
        IdentityStore clientStore = new(ConfigDirectories.ClientOwnedIdentitiesPath, true);

        foreach (IdentityLabel label in clientStore.AvailableIdentities)
        {
            this.ClientAvailableIdentities.Add(label);
        }

        IdentityStore serverStore = new(ConfigDirectories.ServerOwnedIdentitiesPath, false);

        foreach (IdentityLabel label in serverStore.AvailableIdentities)
        {
            this.ServerAvailableIdentities.Add(label);
        }
    }

    [RelayCommand]
    public void GoToClientScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));

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

        this.ChangePage(this, new ServerViewModel(identity, this.ServerSharedFolder, password));
    }

    [RelayCommand]
    public void GoToAboutScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToSettingsScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));
}
