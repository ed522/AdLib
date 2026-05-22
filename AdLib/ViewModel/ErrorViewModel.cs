using System;

using AdLib.View;
using AdLib.ViewModel.Core;

namespace AdLib.ViewModel;

public class ErrorViewModel(string message) : PageViewModel
{
    public ErrorViewModel() : this("An unknown error occurred.") { }
    public override Type ViewType => typeof(Error);
    public override string Title => "Error!";
    public string Message { get; set; } = message;
}
