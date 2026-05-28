using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.ComponentModel;

using IdentityCreationModal = AdLib.View.Modal.IdentityCreationModal;

namespace AdLib.ViewModel.Modal;

public partial class IdentityCreationModalViewModel : ModalViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(IdentityCreationModal);

    public override string Title => "Create a new identity";
    public override bool CanCloseWithoutAction => true;

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
    public override event EventHandler<SwitchedEventArgs>? Switched;
}
