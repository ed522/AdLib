using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View.Page;
using AdLib.ViewModel.Core;

using CommunityToolkit.Mvvm.Input;

namespace AdLib.ViewModel.Page;

public partial class AboutScreenViewModel : PageViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(AboutScreen);

    public override string Title => "About";

    public override bool IsWorking
    {
        get => false;
        protected set => throw new NotSupportedException();
    }

    [RelayCommand]
    public void Return() => this.ChangePage(new StartScreenViewModel());
}