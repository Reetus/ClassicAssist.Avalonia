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
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using ClassicAssist.Data;

namespace ClassicAssist.Avalonia.Misc;

public class WindowInitializedBehaviour : Behavior<Window>
{
    protected override void OnAttached()
    {
        base.OnAttached();

        if ( AssociatedObject == null )
        {
            return;
        }

        // Load on Opened, not Initialized. Xaml.Behaviors 12 attaches this behaviour while
        // MainWindow.axaml is still being populated: Initialized has already been raised by then (so
        // a handler for it never runs), and the tab controls further down the file - whose view
        // models are the settings providers - do not exist yet, so loading in OnAttached reaches no
        // provider. Either way nothing deserializes, and the save on close then rewrites the profile
        // without the providers' sections. Opened is raised from Show(), once the whole tree has been
        // built and before App calls CompleteStartup, so a linked-profile switch replayed there
        // still runs after this load.
        AssociatedObject.Opened += OnOpened;
        AssociatedObject.Closing += OnClosing;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        if ( AssociatedObject == null )
        {
            return;
        }

        AssociatedObject.Opened -= OnOpened;
        AssociatedObject.Closing -= OnClosing;
    }

    private static void OnClosing( object sender, CancelEventArgs e )
    {
        Options.Save( Options.CurrentOptions );
        AssistantOptions.Save();
    }

    private void OnOpened( object sender, EventArgs e )
    {
        // Once only: the main window is shown once, but a re-show must not reload over live state.
        AssociatedObject?.Opened -= OnOpened;
        AssistantOptions.OnWindowLoaded();
    }
}
