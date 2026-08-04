#region License

// Copyright (C) 2022 Reetus
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

namespace ClassicAssist.Data.Hotkeys
{
    /// <summary>
    ///     Marks a property on a <see cref="Commands.HotkeyCommand" /> as user-configurable. The property is
    ///     surfaced as a control in the hotkey Options dialog and round-tripped through the profile's
    ///     Hotkeys/Options array.
    ///     <para>
    ///         <see cref="BaseType" /> selects which control the dialog builds - only <see cref="Enum" /> is
    ///         handled today, matching upstream. <see cref="Type" /> is the concrete property type, used both
    ///         to enumerate the choices and to deserialize the stored value back.
    ///     </para>
    /// </summary>
    public class HotkeyConfigurationAttribute : Attribute
    {
        public Type BaseType { get; set; }
        public string Name { get; set; }
        public Type Type { get; set; }
    }
}
