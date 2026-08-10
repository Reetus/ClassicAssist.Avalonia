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

using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Shared.UI.ViewModels.Debug;

/// <summary>
///     Lets a caller outside the Debug Window pre-populate one of its tabs - e.g. Object Inspector's
///     "double-click a Properties row" opens the Property tab with <see cref="Object" /> set to the
///     entity's property list. Ported from the WPF build's DebugBaseViewModel.
/// </summary>
public class DebugBaseViewModel : BaseViewModel
{
    public object Object
    {
        get;
        set => SetProperty( ref field, value );
    }
}
