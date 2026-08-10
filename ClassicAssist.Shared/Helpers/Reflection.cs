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
using System.Reflection;
using ClassicAssist.Shared;

namespace ClassicAssist.Helpers;

public static class Reflection
{
    public static T GetTypePropertyValue<T>( Type type, string property, object obj = null,
        BindingFlags bindingFlags = BindingFlags.Default )
    {
        // Default is the no-flags overload, which resolves public instance and static properties.
        // Passing BindingFlags.Default to GetProperty( string, BindingFlags ) would instead match
        // nothing, so the two are not interchangeable.
        PropertyInfo propertyInfo = bindingFlags == BindingFlags.Default
            ? type.GetProperty( property )
            : type.GetProperty( property, bindingFlags );

        T val = (T) propertyInfo?.GetValue( obj, null );

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if ( val == null )
        {
            return default;
        }

        return val;
    }

    public static T GetTypePropertyValue<T>( string type, string property, object obj,
        BindingFlags bindingFlags = BindingFlags.Default, Assembly assembly = null )
    {
        if ( assembly == null )
        {
            assembly = Engine.ClassicAssembly;
        }

        Type t = assembly?.GetType( type );

        return t == null ? default : GetTypePropertyValue<T>( t, property, obj, bindingFlags );
    }
}