using System;

namespace AdLib.ViewModel;

public class ErrorViewModel(string message) : PageViewModelBase
{
    public override Type ViewType => typeof(View.Error);
    public override string Title => "Error!";
    public string Message { get; set; } = message;

    public ErrorViewModel() : this("An unknown error occurred.") {}
}
