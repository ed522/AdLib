using System;
using System.Threading.Tasks;

namespace AdLib.ViewModel.Core;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public event EventHandler<PageViewModel>? PageChanged;
    public event EventHandler<ModalViewModel>? ModalOpened;
    public event EventHandler<ModalViewModel>? ModalClosed;

    private readonly TaskCompletionSource<ModalViewModel> _modalTask = new();

    public PageViewModel() => this.ModalClosed += (_, vm) => this._modalTask.TrySetResult(vm);

    protected void ChangePage(object? caller, PageViewModel viewModel)
    {
        this.PageChanged?.Invoke(caller, viewModel);
    }

    protected void OpenModal(ModalViewModel viewModel) =>
        this.ModalOpened?.Invoke(this, viewModel);

    protected async Task<ModalViewModel> OpenModalAsync(ModalViewModel viewModel)
    {
        viewModel.Closed += (_, _) => this.ModalClosed?.Invoke(this, viewModel);
        this.ModalOpened?.Invoke(this, viewModel);
        return await this._modalTask.Task;
    }
}
