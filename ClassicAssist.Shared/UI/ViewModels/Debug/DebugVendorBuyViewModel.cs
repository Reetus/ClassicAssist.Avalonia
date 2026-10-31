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
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Data.Vendors;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Network;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

public class DebugVendorBuyViewModel : BaseViewModel, IDisposable
{
    /// <summary>Guards <see cref="Dispose" /> so the teardown below can only ever run once.</summary>
    private bool _disposed;

    /// <summary>
    ///     Unsubscribes the vendor-buy notifications for this window and drops the collected
    ///     messages. Called once from <c>DebugWindow.OnClosed</c>. Unsubscribes whether or not
    ///     <see cref="IsEnabled" /> is on - a plain <c>-=</c>, harmless when nothing is subscribed -
    ///     so a capture left running at close cannot keep handling vendor traffic forever; the
    ///     setter then becomes inert rather than re-subscribing, so disposal wins over the toggle.
    /// </summary>
    public void Dispose()
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;

        IncomingPacketHandlers.VendorBuyDisplayEvent -= OnVendorBuyDisplayEvent;
        Messages.Clear();
    }

    public bool IsEnabled
    {
        get;
        set
        {
            // Inert once disposed - see Dispose: a late toggle must not resurrect the subscription
            // on a torn-down window.
            if ( _disposed )
            {
                return;
            }

            if ( value != field )
            {
                if ( value )
                {
                    IncomingPacketHandlers.VendorBuyDisplayEvent += OnVendorBuyDisplayEvent;
                }
                else
                {
                    IncomingPacketHandlers.VendorBuyDisplayEvent -= OnVendorBuyDisplayEvent;
                }
            }

            SetProperty( ref field, value );
        }
    }

    public ObservableCollection<string> Messages
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    private void OnVendorBuyDisplayEvent( int serial, ShopListEntry[] entries )
    {
        _dispatcher.Invoke( () =>
        {
            Messages.Add( $"Vendor: Serial: 0x{serial:x}" );

            foreach ( ShopListEntry entry in entries )
            {
                bool isCliloc = entry.Name.All( char.IsDigit );

                Messages.Add(
                    $"\tName: {entry.Name} {( isCliloc ? $"({Cliloc.GetProperty( int.Parse( entry.Name ) )})" : string.Empty )}, Amount: {entry.Amount}, Price: {entry.Price}, ItemID: {entry.Item.ID} (0x{entry.Item.ID:x})" );
            }
        } );
    }
}