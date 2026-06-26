using System;
using System.IO;
using System.Text;

using Avalonia;

using NLog;

namespace AdLib;

internal static class Program
{
    private static readonly Logger Log = LogManager.GetLogger(nameof(Program));
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("AdLib.Assets.nlog.config");

        if (stream is null)
        {
            throw new FileNotFoundException("Failed to load nlog.config");
        }

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data, 0, data.Length);
        LogManager.Setup().LoadConfigurationFromXml(Encoding.UTF8.GetString(data));
        LogManager.ReconfigExistingLoggers();

        Log.Info("Starting AdLib");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
#if DEBUG
                     .WithDeveloperTools()
#endif
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();
}
