using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View.Page;
using AdLib.ViewModel.Core;

namespace AdLib.ViewModel.Page;

public class ErrorViewModel(string message) : PageViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(Error);
    
    public ErrorViewModel() : this("An unknown error occurred.") { }
    public override string Title => "Error!";

    public override bool IsWorking
    {
        get => false;
        protected set { }
    }

    public string Message { get; set; } = message;
}
