using System;

namespace AdLib.ViewModel;

public class ErrorViewModel : PageViewModelBase
{
    public override Type ViewType => typeof(View.Error);
    public override string Title => "Error!";
    public string Message { get; set; }
    
    public ErrorViewModel(string message)
    {
        this.Message = message;
    }

    public ErrorViewModel() : this("An unknown error occurred.") {}
}
