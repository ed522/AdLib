using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View.Modal;
using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AdLib.ViewModel.Modal;

public partial class UserPasswordModalViewModel(string title, string message) : ModalViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(UserPasswordModal);

    public override string Title { get; } = title;
    public override bool CanCloseWithoutAction => true;
    public string Message { get; } = message;

    [ObservableProperty] public string _name = "";
    [ObservableProperty] public string _password = "";

    public void Close() =>
        this.Closed?.Invoke(this, new ClosedEventArgs
        {
            Action = CloseAction.ClosedWithoutAction,
            Data = this,
        });

    public void CloseWithReason(CloseAction action) =>
        this.Closed?.Invoke(this, new ClosedEventArgs
        {
            Action = action,
            Data = this,
        });

    public void OnCancel() => this.CloseWithReason(CloseAction.Cancel);
    public void OnSubmit() => this.CloseWithReason(CloseAction.Submit);

    public override event EventHandler<ClosedEventArgs>? Closed;

    public override event EventHandler<SwitchedEventArgs>? Switched
    {
        add { }
        remove { }
    }
}
