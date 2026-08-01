using System;
using Avalonia;

namespace ClassicAssist.Launcher
{
    internal class Program
    {
        [STAThread]
        public static void Main( string[] args )
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }
    }
}
