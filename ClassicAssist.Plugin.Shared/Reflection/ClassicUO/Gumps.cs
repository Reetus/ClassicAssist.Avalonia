#region License

// Copyright (C) 2021 Reetus
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

using System.Collections.Generic;
using System.Linq;

namespace ClassicAssist.Plugin.Shared.Reflection.ClassicUO
{
    public static class Gumps
    {
        /// <summary>
        ///     Re-resolved on every call rather than cached: this used to be a <c>static readonly</c>
        ///     field set from a static constructor, which locked in whatever
        ///     <c>UIManager.Gumps</c> resolved to (including a permanent <c>null</c>) the first time
        ///     anything touched this type - there was no way to recover if that first call happened to
        ///     run before <see cref="ReflectionImpl.DefaultAssembly" /> was set, or against a client
        ///     shape it didn't expect. The property lookup itself is cheap.
        /// </summary>
        public static IEnumerable<dynamic> GetGumps()
        {
            dynamic gumps = Reflections.Helpers.ReflectionHelper.GetTypePropertyValue<dynamic>( "ClassicUO.Game.Managers.UIManager", "Gumps", null );

            return ( (IEnumerable<dynamic>) gumps )?.ToList();
        }
    }
}