using System;

using AdLib.View;

namespace AdLib.ViewModel;

public class ErrorViewModel(string message) : PageViewModelBase
{
    public ErrorViewModel() : this("An unknown error occurred.") { }
    public override Type ViewType => typeof(Error);
    public override string Title => "Error!";
    public string Message { get; set; } = message;
}
