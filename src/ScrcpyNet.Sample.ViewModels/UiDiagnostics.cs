using System;
using System.IO;

namespace ScrcpyNet.Sample.ViewModels
{
    /// <summary>
    /// Lightweight file-based diagnostics for the running app. Log files live in
    /// %AppData%\ScrcpyNet\ so they can be inspected while the app runs.
    /// </summary>
    public static class UiDiagnostics
    {
        public static string Dir { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrcpyNet");

        public static string LogFile => Path.Combine(Dir, "debug.log");

        static UiDiagnostics()
        {
            try { Directory.CreateDirectory(Dir); } catch { /* ignore */ }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never break the app.
            }
        }
    }
}
