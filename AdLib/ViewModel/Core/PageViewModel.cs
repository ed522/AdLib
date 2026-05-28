using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AdLib.ViewModel.Core;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    public class PageChangeRequestedEventArgs : EventArgs
    {
        public required PageViewModel NewPage { get; init; }
    }

    public class ModalOpenRequestedEventArgs
    {
        public required ModalViewModel Modal { get; init; }
    }

    public class ModalFinishedEventArgs : EventArgs
    {
        public required ModalViewModel OldModal { get; init; }
        public required ModalViewModel? NewModal { get; init; }
        public required ModalViewModel.CloseAction Action { get; init; }
    }

    public event EventHandler<PageChangeRequestedEventArgs>? PageChangeRequested;
    public event EventHandler<ModalOpenRequestedEventArgs>? ModalOpenRequested;
    public event EventHandler<ModalFinishedEventArgs>? ModalFinished;

    private readonly TaskCompletionSource<ModalViewModel?> _modalTask = new();

    public PageViewModel() => this.ModalFinished += (_, args) => this._modalTask.TrySetResult(args.OldModal);

    protected void ChangePage(PageViewModel page)
    {
        this.PageChangeRequested?.Invoke(this, new PageChangeRequestedEventArgs { NewPage = page });
    }

    protected void OpenModal(ModalViewModel modal) =>
        this.ModalOpenRequested?.Invoke(this, new ModalOpenRequestedEventArgs { Modal = modal });

    protected async Task<ModalViewModel?> OpenModalAsync(ModalViewModel modal)
    {
        modal.Closed += (_, args) => this.ModalFinished?.Invoke(this, new ModalFinishedEventArgs
        {
            OldModal = args.Data,
            NewModal = null,
            Action = args.Action,
        });

        modal.Switched += (_, args) => this.ModalFinished?.Invoke(this, new ModalFinishedEventArgs
        {
            OldModal = args.Data,
            NewModal = args.NewPage,
            Action = args.Action,
        });

        this.ModalOpenRequested?.Invoke(this, new ModalOpenRequestedEventArgs { Modal = modal });
        return await this._modalTask.Task;
    }
}
