using System;

namespace AdLib.ViewModel;

public abstract class PageViewModelBase : ViewModelBase
{
    public abstract string Title { get; }
    public event EventHandler<PageViewModelBase>? OnPageChanged;

    protected void ChangePage(object? caller, PageViewModelBase viewModel)
    {
        this.OnPageChanged?.Invoke(caller, viewModel);
    }
}
