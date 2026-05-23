using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View.Templates;
using AdLib.ViewModel.Core;

namespace AdLib.ViewModel.Templates;

public class ModalFrameViewModel(ModalViewModel modal) : ViewModelBase
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(ModalFrame);
}
