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
        PageViewModelBase viewModel = new StartScreenViewModel();
        viewModel.OnPageChanged += (_ /* sender */, arg) => this.HandlePageChange(arg);
        this.CurrentPage = viewModel;
        
    }

    private void HandlePageChange(PageViewModelBase arg) => this.CurrentPage = arg;

}
