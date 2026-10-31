#region License

// Copyright (C) 2020 Reetus
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Threading.Tasks;

namespace ClassicAssist.Shared;

public interface IUIInvoker
{
    Task Invoke( string typeName, object[] ctorParam = null, Type dataContextType = null,
        object[] dataContextParam = null );
    Task InvokeDialog<T>( string typeName, object[] ctorParam = null, T dataContext = default ) where T : class;

    /// <summary>
    ///     Opens the hue picker dialog. The result distinguishes "cancelled" from a genuine hue 0;
    ///     see <see cref="HueChoice" />.
    /// </summary>
    Task<HueChoice> GetHueAsync();

    Task<string> ShowOpenFileDialogAsync( string title, string filterName, string[] extensions );
    void SetClipboardText( string text );

    /// <summary>
    ///     Returns the current clipboard text, or null when there is none. Async end-to-end so
    ///     callers on the UI thread can await it instead of blocking on the dispatcher.
    /// </summary>
    Task<string> GetClipboardText();
}

/// <summary>
///     Result of a hue picker dialog. <see cref="Hue" /> is null when the user cancelled, so a
///     cancelled dialog is distinguishable from a genuine hue 0 - callers that write the hue into
///     settings must leave the old value untouched when <see cref="Cancelled" /> is true.
///     <para>
///         The implicit conversion to <c>int</c> exists only for legacy call sites ported from WPF
///         with a <c>-1</c> cancel check; it yields the chosen hue, or -1 when cancelled.
///     </para>
/// </summary>
public readonly struct HueChoice
{
    private HueChoice( int hue )
    {
        Hue = hue;
    }

    /// <summary>
    ///     True when the user dismissed the dialog without picking a hue.
    /// </summary>
    public bool Cancelled => !Hue.HasValue;

    /// <summary>
    ///     The chosen hue, or null when the dialog was cancelled.
    /// </summary>
    public int? Hue { get; }

    /// <summary>
    ///     Creates a result for a hue the user actually chose.
    /// </summary>
    public static HueChoice FromHue( int hue )
    {
        return new HueChoice( hue );
    }

    /// <summary>
    ///     Legacy <c>int</c> conversion for the call sites that still test for -1 on cancel: yields
    ///     the chosen hue, or -1 when cancelled (a genuine hue 0 stays 0).
    /// </summary>
    public static implicit operator int( HueChoice choice )
    {
        return choice.Hue ?? -1;
    }
}