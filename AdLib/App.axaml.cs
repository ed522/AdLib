using AdLib.View;
using AdLib.ViewModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AdLib;

public class App : Application
{
    public static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#252530"));
    public static readonly IBrush DimmedBackgroundBrush = new SolidColorBrush(Color.Parse("#101010"), 0.7);

    public static readonly IBrush SubtleLineBrush = new SolidColorBrush(Color.Parse("#434352"));
    public static readonly IBrush BrightLineBrush = new SolidColorBrush(Color.Parse("#727272"));

    public static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#F0F0F0"));
    public static readonly IBrush SubtleTextBrush = new SolidColorBrush(Color.Parse("#AFAFAF"));

    public static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#DBA418"));
    public static readonly IBrush LightAccentBrush = new SolidColorBrush(Color.Parse("#E8C46B"));
    
    public override void Initialize() { AvaloniaXamlLoader.Load(this); }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            desktop.MainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
