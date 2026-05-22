using System;

namespace AdLib.ViewModel.Core;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public event EventHandler<PageViewModel>? OnPageChanged;

    protected void ChangePage(object? caller, PageViewModel viewModel)
    {
        this.OnPageChanged?.Invoke(caller, viewModel);
    }
}
