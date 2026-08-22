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

using ClassicAssist.Shared.UI;

namespace ClassicAssist.Data.Screenshot;

/// <summary>
///     One body graphic the mobile-death screenshot trigger will fire for.
/// </summary>
public class ScreenshotMobileFilterEntry : SetPropertyNotifyChanged
{
    public bool Enabled
    {
        get;
        set => SetProperty( ref field, value );
    }

    public int ID
    {
        get;
        set => SetProperty( ref field, value );
    }

    public string Note
    {
        get;
        set => SetProperty( ref field, value );
    }
}
