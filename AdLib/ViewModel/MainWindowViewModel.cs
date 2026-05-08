using System;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel;

public partial class MainWindowViewModel : ViewModelBase
{
    public override Type ViewType => typeof(View.MainWindow);

    [ObservableProperty] private ObservableObject _currentPage;

    public MainWindowViewModel()
    {
        this._currentPage = null!; // set by CurrentPage setter (but that calls SetProperty which throws off
                                   // the null check)
        this.HandlePageChange(new StartScreenViewModel());
    }

    private void HandlePageChange(PageViewModelBase arg)
    {
        arg.OnPageChanged += (_ /* sender */, next) => this.HandlePageChange(next);
        this.CurrentPage = arg;
    }

}
