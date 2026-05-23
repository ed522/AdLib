using System;

namespace AdLib.ViewModel.Core;

public abstract class ModalViewModel : ViewModelBase
{
    public enum CloseAction
    {
        ClosedWithoutAction = 0,
        Ok,
        Cancel,
        Yes,
        No,
        Close,
        Abort,
        Retry,
        Ignore,
        Continue,
        TryAgain,
    }

    public class ClosedEventArgs : EventArgs
    {
        public required ModalViewModel Data { get; init; }
        public required CloseAction Action { get; init; }
    }

    public class SwitchedEventArgs : EventArgs
    {
        public required ModalViewModel Data { get; init; }
        public required ModalViewModel NewPage { get; init; }
        public required CloseAction Action { get; init; }
    }

    public abstract string Title { get; }
    public abstract bool CanCloseWithoutAction { get; }

    public abstract event EventHandler<ClosedEventArgs>? Closed;
    public abstract event EventHandler<SwitchedEventArgs>? Switched;
}
