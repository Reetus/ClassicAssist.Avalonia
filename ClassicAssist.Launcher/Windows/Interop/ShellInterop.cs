using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClassicAssist.Launcher.Windows.Interop;

/// <summary>
///     Raw COM declarations for Windows' custom taskbar Jump List API (ICustomDestinationList and
///     friends). There is no managed wrapper for this outside WPF's own System.Windows.Shell.JumpList
///     (which does not exist here), so this is the standard MSDN "Custom Destination Lists" recipe,
///     transcribed directly rather than pulled from a package. Method order in every interface below
///     is load-bearing: these are vtable-dispatched COM interfaces, so it must match the native
///     declaration order exactly (shobjidl_core.h / objectarray.h / propsys.h) or calls silently
///     invoke the wrong member.
/// </summary>
[SupportedOSPlatform( "windows" )]
internal static class ShellGuids
{
    public static readonly Guid CLSID_DestinationList = new( "77F10CF0-3DB5-4966-B520-B7C54FD35ED6" );
    public static readonly Guid CLSID_EnumerableObjectCollection = new( "2D3468C1-36A7-43B6-AC24-D3F02FD9607A" );
    public static readonly Guid CLSID_ShellLink = new( "00021401-0000-0000-C000-000000000046" );

    public static readonly Guid IID_IObjectArray = new( "92CA9DCD-5622-4BBA-A805-5E9F541BD8C9" );
    public static readonly Guid IID_IShellLinkW = new( "000214F9-0000-0000-C000-000000000046" );
}

[SupportedOSPlatform( "windows" )]
internal enum KnownDestCategory
{
    Frequent = 1,
    Recent = 2
}

[SupportedOSPlatform( "windows" )]
[ComImport]
[Guid( "6332DEBF-87B5-4670-90C0-5E57B408A49E" )]
[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
internal interface ICustomDestinationList
{
    void SetAppID( [MarshalAs( UnmanagedType.LPWStr )] string appId );

    void BeginList( out uint maxSlots, [In] ref Guid riid, [MarshalAs( UnmanagedType.Interface )] out object removedItems );

    void AppendCategory( [MarshalAs( UnmanagedType.LPWStr )] string category, IObjectArray items );

    void AppendKnownCategory( KnownDestCategory category );

    void AddUserTasks( IObjectArray items );

    void CommitList();

    void GetRemovedDestinations( [In] ref Guid riid, [MarshalAs( UnmanagedType.Interface )] out object items );

    void DeleteList( [MarshalAs( UnmanagedType.LPWStr )] string appId );

    void AbortList();
}

[SupportedOSPlatform( "windows" )]
[ComImport]
[Guid( "92CA9DCD-5622-4BBA-A805-5E9F541BD8C9" )]
[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
internal interface IObjectArray
{
    void GetCount( out uint count );

    void GetAt( uint index, [In] ref Guid riid, [MarshalAs( UnmanagedType.Interface )] out object item );
}

[SupportedOSPlatform( "windows" )]
[ComImport]
[Guid( "5632B1A4-E38A-400A-928A-D4CD63230295" )]
[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
internal interface IObjectCollection
{
    // IObjectArray's two members come first in the native vtable - IObjectCollection extends it.
    void GetCount( out uint count );

    void GetAt( uint index, [In] ref Guid riid, [MarshalAs( UnmanagedType.Interface )] out object item );

    void AddObject( [MarshalAs( UnmanagedType.IUnknown )] object item );

    void AddFromArray( IObjectArray source );

    void RemoveObjectAt( uint index );

    void Clear();
}

[SupportedOSPlatform( "windows" )]
[ComImport]
[Guid( "000214F9-0000-0000-C000-000000000046" )]
[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
internal interface IShellLinkW
{
    void GetPath( [Out, MarshalAs( UnmanagedType.LPWStr )] StringBuilder file, int maxPath, IntPtr findData, uint flags );
    void GetIDList( out IntPtr idList );
    void SetIDList( IntPtr idList );
    void GetDescription( [Out, MarshalAs( UnmanagedType.LPWStr )] StringBuilder name, int maxName );
    void SetDescription( [MarshalAs( UnmanagedType.LPWStr )] string name );
    void GetWorkingDirectory( [Out, MarshalAs( UnmanagedType.LPWStr )] StringBuilder dir, int maxPath );
    void SetWorkingDirectory( [MarshalAs( UnmanagedType.LPWStr )] string dir );
    void GetArguments( [Out, MarshalAs( UnmanagedType.LPWStr )] StringBuilder args, int maxPath );
    void SetArguments( [MarshalAs( UnmanagedType.LPWStr )] string args );
    void GetHotkey( out short hotkey );
    void SetHotkey( short hotkey );
    void GetShowCmd( out int showCmd );
    void SetShowCmd( int showCmd );
    void GetIconLocation( [Out, MarshalAs( UnmanagedType.LPWStr )] StringBuilder iconPath, int iconPathLength, out int iconIndex );
    void SetIconLocation( [MarshalAs( UnmanagedType.LPWStr )] string iconPath, int iconIndex );
    void SetRelativePath( [MarshalAs( UnmanagedType.LPWStr )] string relativePath, uint reserved );
    void Resolve( IntPtr hwnd, uint flags );
    void SetPath( [MarshalAs( UnmanagedType.LPWStr )] string path );
}

[SupportedOSPlatform( "windows" )]
[ComImport]
[Guid( "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99" )]
[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
internal interface IPropertyStore
{
    void GetCount( out uint count );
    void GetAt( uint index, out PropertyKey key );
    void GetValue( [In] ref PropertyKey key, [Out] PropVariant value );
    void SetValue( [In] ref PropertyKey key, [In] PropVariant value );
    void Commit();
}

[SupportedOSPlatform( "windows" )]
[StructLayout( LayoutKind.Sequential )]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;

    public PropertyKey( Guid formatId, int propertyId )
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    // PKEY_Title - what the jump list uses as the item's display text.
    public static readonly PropertyKey Title = new( new Guid( "F29F85E0-4FF9-1068-AB91-08002B27B3D9" ), 2 );
}

/// <summary>
///     Minimal PROPVARIANT covering only the VT_LPWSTR case this launcher needs (PKEY_Title). Marshaled
///     as a class (a by-reference struct) so SetValue can hand the native side a pointer to it directly.
/// </summary>
[SupportedOSPlatform( "windows" )]
[StructLayout( LayoutKind.Explicit )]
internal sealed class PropVariant : IDisposable
{
    private const ushort VT_LPWSTR = 31;

    [FieldOffset( 0 )]
    private ushort _vt;

    [FieldOffset( 8 )]
    private IntPtr _pointerValue;

    public static PropVariant FromString( string value )
    {
        return new PropVariant { _vt = VT_LPWSTR, _pointerValue = Marshal.StringToCoTaskMemUni( value ) };
    }

    [DllImport( "ole32.dll" )]
    private static extern int PropVariantClear( PropVariant pvar );

    public void Dispose()
    {
        PropVariantClear( this );
        GC.SuppressFinalize( this );
    }

    ~PropVariant()
    {
        PropVariantClear( this );
    }
}
