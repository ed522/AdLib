using System;

using AdLib.Config;
using AdLib.Identities;
using AdLib.View;

using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel;

public partial class StartScreenViewModel : PageViewModelBase
{
    public override Type ViewType => typeof(StartScreen);
    public override string Title => "Welcome";

    public string ClientTargetIp { get; set; } = "";
    public string ServerSharedFolder { get; set; } = "";

    // TODO allow server identity switching
    private static Guid _defaultServerIdentityName => 
        throw new NotImplementedException("Unable to create a default server identity name");

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
            _defaultServerIdentityName,
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
