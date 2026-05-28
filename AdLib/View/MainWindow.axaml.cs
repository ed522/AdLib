using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AdLib.View;

public partial class MainWindow : Window
{
    public static readonly IBrush DisabledBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64), 0.7);

    private readonly Border? _blockingOverlay;

    public MainWindow()
    {
        this.InitializeComponent();
        this._blockingOverlay = this.FindControl<Border>("BlockingOverlay");
    }

    private void BlockingOverlay_OnKeyDown(object? sender, KeyEventArgs e)
    {
        // just block it
        e.Handled = true;
    }

    private void BlockingOverlay_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(IsVisible))
        {
            this._blockingOverlay?.Focus();
        }
    }
}
