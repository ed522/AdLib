using System;
using System.Diagnostics.CodeAnalysis;

using AdLib.View;
using AdLib.ViewModel.Core;

namespace AdLib.ViewModel;

public class ErrorViewModel(string message) : PageViewModel
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public override Type ViewType => typeof(Error);
    
    public ErrorViewModel() : this("An unknown error occurred.") { }
    public override string Title => "Error!";
    public string Message { get; set; } = message;
}
