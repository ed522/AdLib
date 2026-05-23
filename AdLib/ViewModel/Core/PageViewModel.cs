using System;

namespace AdLib.ViewModel.Core;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public event EventHandler<PageViewModel>? PageChanged;
    public event EventHandler<ModalViewModel>? ModalOpened;

    protected void ChangePage(object? caller, PageViewModel viewModel)
    {
        this.PageChanged?.Invoke(caller, viewModel);
    }

    protected void OpenModal(object? caller, ModalViewModel viewModel) { this.ModalOpened?.Invoke(caller, viewModel); }

}
