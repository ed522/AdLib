using System;

using AdLib.View;

using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel;

public partial class StartScreenViewModel : PageViewModelBase
{
    public override Type ViewType => typeof(StartScreen);
    public override string Title => "Welcome";

    public string TargetIp { get; set; } = "";
    public string SharedFolder { get; set; } = "";

    [RelayCommand]
    public void GoToClientScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToServerScreen() =>
        this.ChangePage(this, new ServerViewModel());

    [RelayCommand]
    public void GoToAboutScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));

    [RelayCommand]
    public void GoToSettingsScreen() =>
        this.ChangePage(this, new ErrorViewModel("Not implemented"));
}
