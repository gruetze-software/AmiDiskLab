using Avalonia;
using System;
using System.Diagnostics;
using System.IO;

namespace AmiDiskLab.App
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Opt-in persistent diagnostics, including Release builds.
            var logPath = Environment.GetEnvironmentVariable("AMIDISKLAB_LOG_PATH");
            TextWriterTraceListener? listener = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    try
                    {
                        listener = new TextWriterTraceListener(new StreamWriter(logPath, append: true));
                        Trace.Listeners.Add(listener);
                        Trace.AutoFlush = true;
                        Trace.WriteLine($"AmiDiskLab started {DateTimeOffset.Now:O}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { Trace.WriteLine($"Cannot open diagnostic log: {ex.Message}"); }
                }
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                if (listener is not null)
                {
                    Trace.Listeners.Remove(listener);
                    listener.Dispose();
                }
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
                => AppBuilder.Configure<App>()
                        .UsePlatformDetect()
                        .WithInterFont()
                        .LogToTrace();
    }
}
