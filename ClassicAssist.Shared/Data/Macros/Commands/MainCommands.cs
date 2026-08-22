using System;
using System.IO;
using System.Linq;
using System.Threading;
using ClassicAssist.Shared;
using ClassicAssist.Data.Hotkeys;
using ClassicAssist.Data.Screenshot;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO;
using ClassicAssist.UO.Network.Packets;
using ClassicAssist.UO.Objects;
using ClassicAssist.UI.ViewModels;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Data.Macros.Commands;

public static class MainCommands
{
    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.OnOff ) } )]
    public static void SetQuietMode( bool onOff )
    {
        MacroManager.QuietMode = onOff;
    }

    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.String ) } )]
    [CommandsDisplayStringSeeAlso( [nameof( Virtues )] )]
    public static void InvokeVirtue( string virtue )
    {
        Virtues v = Utility.GetEnumValueByName<Virtues>( virtue );

        if ( v == Virtues.None )
        {
            return;
        }

        Engine.SendPacketToServer( new InvokeVirtue( v ) );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ) )]
    public static void Resync()
    {
        UOC.Resync();
    }

    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.Timeout ) } )]
    public static void Pause( int milliseconds )
    {
        Thread.Sleep( milliseconds );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ),
        Parameters = new[] { nameof( ParameterType.String ), nameof( ParameterType.Hue ) } )]
    public static void SysMessage( string text, int hue = 0x03b2 )
    {
        UOC.SystemMessage( text, hue );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ),
        Parameters = new[] { nameof( ParameterType.SerialOrAlias ) } )]
    public static void Info( object obj = null )
    {
        int serial = 0;

        if ( obj == null )
        {
            serial = UOC.GetTargetSerialAsync( Strings.Target_object___ ).Result;

            if ( serial == 0 )
            {
                return;
            }
        }

        serial = AliasCommands.ResolveSerial( serial != 0 ? serial : obj );

        if ( serial == 0 )
        {
            return;
        }

        Entity entity = UOMath.IsMobile( serial )
            ? Engine.Mobiles.GetMobile( serial )
            : (Entity) Engine.Items.GetItem( serial );

        if ( entity == null )
        {
            UOC.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        // Was a WPF-era STA thread whose body was never ported, so it opened nothing - and
        // Thread.SetApartmentState throws PlatformNotSupportedException off Windows, which made this
        // command fail outright on Linux. Routed through UIInvoker like Commands.InspectObjectAsync.
        Engine.UIInvoker.Invoke( "ObjectInspectorWindow", null, typeof( ObjectInspectorViewModel ), [entity] );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ),
        Parameters = new[] { nameof( ParameterType.SerialOrAlias ) } )]
    public static void OpenECV( object obj )
    {
        int serial = AliasCommands.ResolveSerial( obj );

        if ( serial == 0 )
        {
            UOC.SystemMessage( Strings.Invalid_container___ );
            return;
        }

        Entity entity = UOMath.IsMobile( serial )
            ? Engine.Mobiles.GetMobile( serial )
            : (Entity) Engine.Items.GetItem( serial );

        if ( entity == null )
        {
            UOC.SystemMessage( Strings.Cannot_find_item___ );
            return;
        }

        ItemCollection collection;

        switch ( entity )
        {
            case Item item:

                // Targeting a container the client has never opened leaves us with nothing to
                // show, so ask the server for its contents first.
                if ( item.Container == null )
                {
                    UOC.WaitForContainerContentsUse( item.Serial, 1000 );
                }

                collection = item.Container ?? new ItemCollection( item.Serial );

                break;
            case Mobile mobile:
                collection = new ItemCollection( entity.Serial ) { mobile.GetEquippedItems() };

                break;
            default:
                collection = new ItemCollection( entity.Serial );

                break;
        }

        // The invoker marshals onto the UI thread itself, so this stays on the macro thread.
        Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null, typeof( EntityCollectionViewerViewModel ),
            [collection] );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.OnOff ) } )]
    public static void Hotkeys( string onOff = "toggle" )
    {
        HotkeyManager manager = HotkeyManager.GetInstance();

        switch ( onOff.Trim().ToLower() )
        {
            case "on":
                {
                    manager.Enabled = true;
                    break;
                }
            case "off":
                {
                    manager.Enabled = false;
                    break;
                }
            default:
                {
                    manager.Enabled = !manager.Enabled;
                    break;
                }
        }

        UOC.SystemMessage( manager.Enabled ? Strings.Hotkeys_enabled___ : Strings.Hotkeys_disabled___,
            manager.Enabled ? 0x3F : 36 );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.OnOff ) } )]
    public static void WarMode( string onOff = "toggle" )
    {
        if ( Engine.Player == null )
        {
            return;
        }

        string onOffNormalized = onOff.Trim().ToLower();

        if ( onOffNormalized != "toggle" )
        {
            switch ( onOffNormalized )
            {
                case "on" when Engine.Player.Status.HasFlag( MobileStatus.WarMode ):
                case "off" when !Engine.Player.Status.HasFlag( MobileStatus.WarMode ):
                    return;
            }
        }

        Engine.SendPacketToServer( Engine.Player.Status.HasFlag( MobileStatus.WarMode )
            ? new WarMode( false )
            : new WarMode( true ) );
    }

    //[CommandsDisplay( Category = nameof( Strings.Main ),
    //    Parameters = new[] { nameof( ParameterType.String ), nameof( ParameterType.String ) } )]
    //public static void MessageBox( string title, string body )
    //{
    //    System.Windows.MessageBox.Show( body, title, MessageBoxButton.OK, MessageBoxImage.Information );
    //}

    [CommandsDisplay( Category = nameof( Strings.Main ) )]
    public static void PlaySound( object param, bool playSync = true )
    {
        switch ( param )
        {
            case int id:
                Engine.SendPacketToClient( new PlaySound( id ) );
                break;
            case string soundFile:
                {
                    string fullPath = Path.Combine( Engine.StartupPath, "Sounds", soundFile );

                    if ( !File.Exists( fullPath ) )
                    {
                        UOC.SystemMessage( Strings.Cannot_find_sound_file___ );
                        return;
                    }

                    AudioPlayback.Play( fullPath, playSync );
                    break;
                }
        }
    }

    [CommandsDisplay( Category = nameof( Strings.Main ) )]
    public static bool Playing()
    {
        MacroManager manager = MacroManager.GetInstance();

        return manager.CurrentMacro != null && ( manager.CurrentMacro.IsRunning || manager.Replay );
    }

    [CommandsDisplay( Category = nameof( Strings.Main ), Parameters = new[] { nameof( ParameterType.MacroName ) } )]
    public static bool Playing( string macroName )
    {
        MacroManager manager = MacroManager.GetInstance();

        MacroEntry macro = manager.Items.FirstOrDefault( m => m.Name.Equals( macroName ) );

        return macro != null && ( macro.IsRunning || manager.Replay );
    }

    /// <summary>
    ///     Takes a screenshot of the client window, returning whether it worked and where it went.
    /// </summary>
    /// <param name="fullscreen">
    ///     Accepted and ignored. Upstream captured either the client window or the whole desktop through
    ///     GDI; here the pixels come from the frame the client itself drew, so there is no desktop to
    ///     include. Kept in the signature so existing macros still run.
    /// </param>
    [CommandsDisplay( Category = nameof( Strings.Main ),
        Parameters = new[]
        {
            nameof( ParameterType.IntegerValue ), nameof( ParameterType.Boolean ), nameof( ParameterType.String )
        } )]
    public static (bool, string) Snapshot( int delay = 0, bool? fullscreen = null, string fileName = "" )
    {
        try
        {
            if ( delay > 0 )
            {
                Thread.Sleep( delay );
            }

            ScreenshotManager manager = ScreenshotManager.GetInstance();

            if ( manager.TakeScreenshot == null )
            {
                UOC.SystemMessage( Strings.Snapshot_failed, (int) SystemMessageHues.Red );

                return ( false, null );
            }

            // Blocking is safe here and not in the tab's own button: macros and hotkeys run on their own
            // threads, and the capture completes on the client's tick with the composer hopping to the
            // UI thread of its own accord.
            string savedTo = manager.TakeScreenshot( string.Empty, fileName ).GetAwaiter().GetResult();

            if ( string.IsNullOrEmpty( savedTo ) )
            {
                UOC.SystemMessage( Strings.Snapshot_failed, (int) SystemMessageHues.Red );

                return ( false, null );
            }

            return ( true, savedTo );
        }
        catch ( Exception e )
        {
            UOC.SystemMessage( e.Message, (int) SystemMessageHues.Red );

            return ( false, null );
        }
    }

    [CommandsDisplay( Category = nameof( Strings.Main ) )]
    public static void Logout()
    {
        ReflectionCommands.Logout();
    }

    [CommandsDisplay( Category = nameof( Strings.Main ) )]
    public static void Quit()
    {
        ReflectionCommands.Quit();
    }
}