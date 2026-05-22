using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

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

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(StartScreen);

    public override string Title => "Welcome";

    public string ClientTargetIp { get; set; } = "";
    public string ServerSharedFolder { get; set; } = "";

    public ObservableCollection<IdentityLabel> ServerAvailableIdentities { get; } = [];
    public ObservableCollection<IdentityLabel> ClientAvailableIdentities { get; } = [];

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
