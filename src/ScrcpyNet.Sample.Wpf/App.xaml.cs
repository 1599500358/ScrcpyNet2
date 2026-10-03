
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using ReactiveUI.Reactive.Builder;
using Serilog;
using System;
using System.IO;
using System.Windows;

namespace ScrcpyNet.Sample.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Registers the WPF platform: binding converters and the dispatcher as the
            // main thread scheduler (ReactiveUI >= 25 requires this explicitly).
            _ = RxAppBuilder.CreateReactiveUIBuilder().WithWpf().BuildApp();

            // Absolute path so the app works no matter which directory it is started from.
            DynamicallyLoadedBindings.LibrariesPath = Path.Combine(AppContext.BaseDirectory, "ScrcpyNet");
            DynamicallyLoadedBindings.Initialize();

            // Enabling debug logging completely obliterates performance
            Log.Logger = new LoggerConfiguration()
                //.MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .CreateLogger();

            base.OnStartup(e);
        }
    }
}
