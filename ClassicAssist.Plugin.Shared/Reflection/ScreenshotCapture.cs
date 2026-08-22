#region License
// Copyright (C) 2026 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
#endregion

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ClassicAssist.Plugin.Shared.Reflections.Helpers;

namespace ClassicAssist.Plugin.Shared.Reflection
{
    /// <summary>
    ///     Screenshots of the client window, read straight out of the graphics device the client draws
    ///     with.
    ///     <para>
    ///         Upstream ClassicAssist captures the client window with GDI - <c>BitBlt</c> from the
    ///         window handle in the plugin header - which is Windows-only and, on every client this
    ///         fork loads into, has no handle to work from: TazUO passes <c>IntPtr.Zero</c> on all
    ///         platforms, ClassicUO only fills it in on Windows, and the bootstrap never does. So
    ///         instead of asking the OS for the window's pixels, this asks FNA for the frame it just
    ///         drew, the same way the client's own PrintScreen handler does. That works identically on
    ///         Windows, Linux and macOS, needs no native dependency of ours, and captures under
    ///         Wayland - where taking another window's pixels is not possible at all.
    ///     </para>
    /// </summary>
    public static partial class ReflectionImpl
    {
        private const string FRAME_DIRECTORY_NAME = "ClassicAssist-Frames";

        /// <summary>
        ///     Sanity bound on a frame, well past any real display: 16k by 16k. Guards the allocation
        ///     below against a nonsense size read back mid-resize.
        /// </summary>
        private const long MAX_FRAME_PIXELS = 16384L * 16384L;

        private const int CAPTURE_TIMEOUT_MS = 5000;

        /// <summary>
        ///     Age at which a leftover frame file is assumed abandoned rather than still in flight.
        /// </summary>
        private static readonly TimeSpan _staleFrameAge = TimeSpan.FromMinutes( 2 );

        /// <summary>
        ///     Whether a screenshot can actually be taken through the client's graphics device.
        ///     <para>
        ///         This probes for the device itself rather than trusting
        ///         <see cref="ClassicAssist.Plugin.PluginEngine.ReflectionAvailable" />, which only
        ///         reports how the plugin was loaded. A NativeAOT ClassicUO loads us managed through its
        ///         bootstrap, so that flag is true there - and the bootstrap ships a decoy managed
        ///         <c>ClassicUO</c> assembly whose <c>Client.Game</c> is a stub <c>GameController</c>
        ///         carrying a single <c>GetScene</c> method, so an assembly-name probe and a type probe
        ///         both pass as well. That client's graphics stack is native code with no managed device
        ///         to read back, and the only place it shows is here: nothing answers to
        ///         <c>GraphicsDevice</c>.
        ///     </para>
        /// </summary>
        public static bool CanCaptureClientFrame()
        {
            return GetGraphicsDevice() != null;
        }

        /// <summary>
        ///     Reads the last frame the client drew and writes it to a temp file, returning null when
        ///     this client cannot be captured.
        ///     <para>
        ///         The read has to happen on the client's own thread - it goes through FNA3D, which is
        ///         not thread safe - so it is queued onto <see cref="TickWorkQueue" /> and this waits for
        ///         that tick. A client that has stopped ticking (mid-reload, or shutting down) resolves
        ///         to null on a timeout rather than leaving the caller waiting on a tick that will never
        ///         come.
        ///     </para>
        /// </summary>
        public static Task<ScreenshotFrame> CaptureClientFrame()
        {
            object device = GetGraphicsDevice();

            if ( device == null )
            {
                return Task.FromResult<ScreenshotFrame>( null );
            }

            TaskCompletionSource<ScreenshotFrame> completion = new TaskCompletionSource<ScreenshotFrame>();

            Enqueue( () =>
            {
                try
                {
                    completion.TrySetResult( CaptureOnClientThread( device ) );
                }
                catch ( Exception e )
                {
                    completion.TrySetException( e );
                }
            } );

            // TrySet* on an already-completed source is a no-op, so the timeout and the capture race
            // harmlessly - whichever lands first wins and the other is dropped.
            Task.Delay( CAPTURE_TIMEOUT_MS ).ContinueWith( _ => completion.TrySetResult( null ) );

            return completion.Task;
        }

        /// <summary>
        ///     Mirrors what the client's own screenshot does: prefer the render target it composes the
        ///     frame into when it has one, and fall back to the backbuffer otherwise. The render target
        ///     is the safer of the two, since a backbuffer's contents are only defined until the swap,
        ///     and older clients that draw straight to the backbuffer still work through the fallback.
        /// </summary>
        private static ScreenshotFrame CaptureOnClientThread( object device )
        {
            object renderTarget = GetScreenRenderTarget();

            int width;
            int height;
            string readMethod;
            object source;

            if ( renderTarget != null )
            {
                source = renderTarget;
                readMethod = "GetData";
                width = GetPropertyValue<int>( renderTarget, "Width" );
                height = GetPropertyValue<int>( renderTarget, "Height" );
            }
            else
            {
                object presentation = GetPropertyValue<object>( device, "PresentationParameters" );

                if ( presentation == null )
                {
                    return null;
                }

                source = device;
                readMethod = "GetBackBufferData";
                width = GetPropertyValue<int>( presentation, "BackBufferWidth" );
                height = GetPropertyValue<int>( presentation, "BackBufferHeight" );
            }

            byte[] pixels = AllocatePixels( width, height );

            if ( pixels == null || !ReadPixels( source, readMethod, pixels ) )
            {
                return null;
            }

            return new ScreenshotFrame { Path = WriteFrame( pixels ), Width = width, Height = height };
        }

        private static byte[] AllocatePixels( int width, int height )
        {
            // A minimised window can report a zero-sized backbuffer, and this multiply is what would
            // turn a nonsense size into an OutOfMemoryException on the client's own thread.
            if ( width <= 0 || height <= 0 || (long) width * height > MAX_FRAME_PIXELS )
            {
                return null;
            }

            return new byte[width * height * 4];
        }

        /// <summary>
        ///     Calls FNA's <c>GetBackBufferData&lt;T&gt;</c> / <c>GetData&lt;T&gt;</c> with T = byte.
        ///     <para>
        ///         Both validate T against the surface format as <c>formatSize % sizeof(T) == 0</c>, so a
        ///         byte array is accepted for the 4-byte <c>SurfaceFormat.Color</c> the client presents
        ///         in, and the pixels land as RGBA with no element conversion on our side. The byte
        ///         count they hand down to FNA3D is the array's own length rather than any count
        ///         argument, which is why it has to be exactly width * height * 4.
        ///     </para>
        /// </summary>
        private static bool ReadPixels( object source, string methodName, byte[] pixels )
        {
            MethodInfo method = source.GetType().GetMethods( BindingFlags.Instance | BindingFlags.Public )
                .FirstOrDefault( m => m.Name == methodName && m.IsGenericMethodDefinition &&
                                      m.GetParameters().Length == 1 );

            if ( method == null )
            {
                return false;
            }

            method.MakeGenericMethod( typeof( byte ) ).Invoke( source, new object[] { pixels } );

            return true;
        }

        /// <summary>
        ///     The client's frame render target, when it has one and it is currently usable - the same
        ///     three conditions the client itself checks before drawing into it. Absent on clients that
        ///     draw straight to the backbuffer, which the field reads answer with null and false.
        /// </summary>
        private static object GetScreenRenderTarget()
        {
            try
            {
                object game = GetGame();

                if ( game == null || !ReflectionHelper.GetTypeFieldValueRecurse<bool>( game.GetType(),
                        "_useScreenRenderTarget", game ) )
                {
                    return null;
                }

                object renderTarget =
                    ReflectionHelper.GetTypeFieldValueRecurse<object>( game.GetType(), "_screenRenderTarget", game );

                if ( renderTarget == null || GetPropertyValue<bool>( renderTarget, "IsDisposed" ) )
                {
                    return null;
                }

                return renderTarget;
            }
            catch
            {
                return null;
            }
        }

        private static object GetGraphicsDevice()
        {
            try
            {
                object game = GetGame();

                return game == null ? null : GetPropertyValue<object>( game, "GraphicsDevice" );
            }
            catch
            {
                return null;
            }
        }

        private static object GetGame()
        {
            return ReflectionHelper.GetTypePropertyValue<object>( "ClassicUO.Client", "Game", null,
                BindingFlags.Static | BindingFlags.Public );
        }

        /// <summary>
        ///     A public property read that answers a missing member with the default rather than
        ///     throwing. <see cref="ReflectionHelper.GetTypePropertyValue{T}(Type,string,object,BindingFlags)" />
        ///     casts the value straight to T, which for a value type is an unboxing cast of null when the
        ///     property is not there - and "not there" is the case that separates a client we can
        ///     capture from one we cannot.
        /// </summary>
        private static T GetPropertyValue<T>( object instance, string name )
        {
            PropertyInfo property = instance?.GetType().GetProperty( name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

            object value = property?.GetValue( instance );

            return value is T typed ? typed : default;
        }

        private static string WriteFrame( byte[] pixels )
        {
            string directory = Path.Combine( Path.GetTempPath(), FRAME_DIRECTORY_NAME );

            Directory.CreateDirectory( directory );
            SweepStaleFrames( directory );

            string path = Path.Combine( directory, $"frame-{Guid.NewGuid():N}.rgba" );

            File.WriteAllBytes( path, pixels );

            return path;
        }

        /// <summary>
        ///     Frames are the reader's to delete, so a UI that died between the capture and the read
        ///     would leave one behind - several megabytes each. Clear out anything old enough that no
        ///     capture could still be in flight.
        /// </summary>
        private static void SweepStaleFrames( string directory )
        {
            try
            {
                DateTime cutoff = DateTime.UtcNow - _staleFrameAge;

                foreach ( string file in Directory.GetFiles( directory, "frame-*.rgba" ) )
                {
                    if ( File.GetLastWriteTimeUtc( file ) < cutoff )
                    {
                        File.Delete( file );
                    }
                }
            }
            catch
            {
                // A file another capture is still writing, or one the UI holds open - either way not
                // worth failing this capture over.
            }
        }
    }
}
