using AdLib.ViewModel.Page;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AdLib.View.Page;

public partial class ClientScreen : UserControl
{
    public ClientScreen() { this.InitializeComponent(); }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        if (this.DataContext is ClientScreenViewModel viewModel)
        {
            _ = viewModel.Initialize();
        }
    }
}
