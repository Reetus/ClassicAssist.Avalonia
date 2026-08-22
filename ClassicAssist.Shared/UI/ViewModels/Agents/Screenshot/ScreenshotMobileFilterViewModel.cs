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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Screenshot;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO;
using ClassicAssist.UO.Objects;
using UOC = ClassicAssist.Shared.UO.Commands;

namespace ClassicAssist.Shared.UI.ViewModels.Agents.Screenshot;

/// <summary>
///     Edits which body graphics the mobile-death trigger fires for. Works on its own copy of the
///     list, which the tab takes over only when OK is clicked.
/// </summary>
public class ScreenshotMobileFilterViewModel : BaseViewModel
{
    public ScreenshotMobileFilterViewModel()
    {
    }

    public ScreenshotMobileFilterViewModel( IEnumerable<ScreenshotMobileFilterEntry> items )
    {
        foreach ( ScreenshotMobileFilterEntry item in items )
        {
            Items.Add( item );
        }
    }

    public ICommand AddCommand => field ??= new RelayCommand( Add, o => true );

    public ObservableCollection<ScreenshotMobileFilterEntry> Items
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ICommand OkCommand => field ??= new RelayCommand( Ok, o => true );

    public ICommand RemoveCommand => field ??= new RelayCommand( Remove, o => o != null );

    public bool Result { get; set; }

    public ScreenshotMobileFilterEntry SelectedItem
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand TargetCommand => field ??= new RelayCommandAsync( Target, o => Engine.Connected );

    private void Add( object obj )
    {
        _dispatcher.Invoke( () => Items.Add( new ScreenshotMobileFilterEntry() ) );
    }

    private void Remove( object obj )
    {
        if ( obj is ScreenshotMobileFilterEntry entry )
        {
            _dispatcher.Invoke( () => Items.Remove( entry ) );
        }
    }

    private void Ok( object obj )
    {
        Result = true;
    }

    /// <summary>
    ///     Targets a mobile and adds its body graphic, which is what the trigger matches on - not its
    ///     serial, so one entry covers every mobile of that kind.
    /// </summary>
    private async Task Target( object arg )
    {
        ( TargetType _, TargetFlags _, int serial, int _, int _, int _, int itemID ) =
            await UOC.GetTargetInfoAsync();

        if ( !UOMath.IsMobile( serial ) )
        {
            return;
        }

        Mobile mobile = Engine.Mobiles.GetMobile( serial );

        string name = mobile?.Name ?? "Unknown";

        _dispatcher.Invoke( () => Items.Add( new ScreenshotMobileFilterEntry { ID = itemID, Note = name } ) );
    }
}
