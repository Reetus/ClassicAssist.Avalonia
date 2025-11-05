using System.IO.Pipes;
using Assistant;
using Avalonia;
using Avalonia.ReactiveUI;
using ClassicAssist.Plugin.Shared;
using StreamJsonRpc;

namespace ClassicAssist.Avalonia
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        public static void Main( string[] args )
        {
            // Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );
            
            if ( args == null || args.Length == 0 )
            {
                return;
            }

            string pipeName = args[0];
            
            // NativeMethods.SetCurrentProcessExplicitAppUserModelID( pipeName );

            NamedPipeClientStream clientStream = new NamedPipeClientStream( ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous );
            clientStream.Connect();

            // Attach client RPC
            Shared.Engine.PluginMethods pluginMethods = new Shared.Engine.PluginMethods();
            JsonRpc rpc = JsonRpc.Attach( clientStream, pluginMethods );
            IHostMethods host = rpc.Attach<IHostMethods>();
            
            Shared.Engine.InstallRPC( rpc, host, pluginMethods );
            Engine.Initialize();
            
            BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().UseReactiveUI();
        }
    }
}