using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View;
using AdLib.ViewModel.Core;

namespace AdLib.ViewModel;

public class NoModalViewModel : ModalViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(NoModal);

    public override string Title => "";
    public override bool CanCloseWithoutAction => false;
    // `Event is never used` - required because they're abstract
#pragma warning disable CS0067
    public override event EventHandler<ClosedEventArgs>? Closed;
    public override event EventHandler<SwitchedEventArgs>? Switched;
#pragma warning restore CS0067
}
