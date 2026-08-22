#region License

// Copyright (C) 2026 Reetus
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
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.UI.Models;
using Xunit;

namespace ClassicAssist.HeadlessTests;

/// <summary>
///     Asserts on filter conditions by constraint name, so a condition that lost its Property reads as
///     a null name in the failure message rather than as a bare count mismatch.
/// </summary>
internal static class FilterAssert
{
    public static void Conditions( EntityCollectionFilterGroup group, BooleanOperation operation,
        params ( string Name, AutolootOperator Operator, int Value )[] expected )
    {
        Assert.Equal( operation, group.Operation );

        Conditions( group.Items, expected );
    }

    public static void Conditions( ObservableCollection<AutolootConstraintEntry> conditions,
        params ( string Name, AutolootOperator Operator, int Value )[] expected )
    {
        List<( string, AutolootOperator, int )> actual =
            conditions.Select( c => ( c.Property?.Name, c.Operator, c.Value ) ).ToList();

        Assert.Equal( expected.Select( e => ( e.Name, e.Operator, e.Value ) ).ToList(), actual );
    }
}
