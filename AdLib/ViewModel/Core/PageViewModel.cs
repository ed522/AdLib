using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AdLib.ViewModel.Modal;

namespace AdLib.ViewModel.Core;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract bool IsWorking { get; protected set; }

    public struct ModalTransitionInfo
    {
        public required ModalViewModel Modal;
        public required ModalViewModel.CloseAction Action;
    }

    private struct InternalModalTransitionInfo
    {
        public required ModalViewModel OldModal;
        public required ModalViewModel? NewModal;
        public required ModalViewModel.CloseAction Action;

        public ModalTransitionInfo ToPublic() => new()
        {
            Modal = this.OldModal,
            Action = this.Action,
        };
    }

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

    private TaskCompletionSource<InternalModalTransitionInfo> _modalTask = new();

    protected PageViewModel() => this.ModalFinished += (_, args) =>
        this._modalTask.TrySetResult(new InternalModalTransitionInfo
        {
            OldModal = args.OldModal,
            NewModal = args.NewModal,
            Action = args.Action,
        });

    protected void ChangePage(PageViewModel page)
    {
        this.PageChangeRequested?.Invoke(this, new PageChangeRequestedEventArgs { NewPage = page });
    }

    protected void OpenModal(ModalViewModel modal) =>
        this.ModalOpenRequested?.Invoke(this, new ModalOpenRequestedEventArgs { Modal = modal });

    private void BindToModal(ModalViewModel modal)
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
    }

    private async Task<InternalModalTransitionInfo> WaitForModalAsync(ModalViewModel modal)
    {
        this.BindToModal(modal);
        InternalModalTransitionInfo info = await this._modalTask.Task;
        this._modalTask = new TaskCompletionSource<InternalModalTransitionInfo>();
        return info;
    }

    public async Task<ModalTransitionInfo> OpenModalAsync(ModalViewModel modal)
    {
        this.ModalOpenRequested?.Invoke(this, new ModalOpenRequestedEventArgs { Modal = modal });
        return (await this.WaitForModalAsync(modal)).ToPublic();
    }

    protected async IAsyncEnumerable<ModalViewModel> OpenModalChainUntilAsync<T>(
        ModalViewModel startingModal
    ) where T : ModalViewModel
    {
        ModalViewModel? currentModal = startingModal;

        this.ModalOpenRequested?.Invoke(this, new ModalOpenRequestedEventArgs { Modal = currentModal });

        InternalModalTransitionInfo transition = await this.WaitForModalAsync(currentModal);
        yield return transition.OldModal;

        do
        {
            currentModal = transition.NewModal;
            if (currentModal is null) yield break;

            transition = await this.WaitForModalAsync(currentModal);
            yield return transition.OldModal;
        } while (currentModal is not T);
    }

    protected async IAsyncEnumerable<ModalViewModel> OpenModalChainUntilEndAsync(ModalViewModel startingModal)
    {
        await foreach (ModalViewModel item in this.OpenModalChainUntilAsync<NoModalViewModel>(startingModal))
        {
            if (item is NoModalViewModel or null) yield break;
            yield return item;
        }
    }
}
