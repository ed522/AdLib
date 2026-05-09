using System;

using AdLib.View;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel;

public partial class ServerViewModel : PageViewModelBase
{
    [ObservableProperty] private string _connectedClient = "None";
    [ObservableProperty] private string _currentTransferName = "No active transfer";
    [ObservableProperty] private string _selectedCertificate = string.Empty;

    [ObservableProperty] private string _selectedPath = string.Empty;
    [ObservableProperty] private string _status = "Disconnected";
    [ObservableProperty] private double _transferProgress;
    public override Type ViewType => typeof(ServerScreen);
    public override string Title => "Hosting Server";

    [RelayCommand]
    private void PauseTransfer() { this.ChangePage(this, new ErrorViewModel("Not implemented")); }

    [RelayCommand]
    private void StopTransfer()
    {
        // TODO implement stopping logic in model
        this.ChangePage(this, new StartScreenViewModel());
    }
}
