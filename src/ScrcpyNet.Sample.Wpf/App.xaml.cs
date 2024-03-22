
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Serilog;
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
            DynamicallyLoadedBindings.LibrariesPath = "ScrcpyNet";
            DynamicallyLoadedBindings.Initialize();
            //ffmpeg.RootPath = "ScrcpyNet";

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
