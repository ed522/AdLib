using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View;
using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel;

public partial class MainWindowViewModel : ViewModelBase
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(MainWindow);
    
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private ModalViewModel _currentModal;
    [ObservableProperty] private string _title = "AdLib";

    public MainWindowViewModel()
    {
        this._currentPage = null!; // set by CurrentPage setter (but that calls SetProperty which throws off
        // the null check)
        this._currentModal = new NoModalViewModel();
        this.HandlePageChange(new StartScreenViewModel());
    }

    private void HandlePageChange(PageViewModel arg)
    {
        arg.PageChanged += (_ /* sender */, next) => this.HandlePageChange(next);
        arg.ModalOpened += (_ /* sender */, next) => { this.HandleModalChange(next); };
        this.Title = $"AdLib - {arg.Title}";
        this.CurrentPage = arg;
    }

    private void HandleModalChange(ModalViewModel arg)
    {
        arg.Closed += (_ /* sender */, _ /* arg */) => this.HandleModalChange(new NoModalViewModel());
        arg.Switched += (_ /* sender */, next) => this.HandleModalChange(next.NewPage);
        this.CurrentModal = arg;
    }
}
