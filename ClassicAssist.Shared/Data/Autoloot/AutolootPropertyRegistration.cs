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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;
using ClassicAssist.UO.Data;
using ClassicAssist.UO.Objects;

namespace ClassicAssist.Data.Autoloot;

/// <summary>
///     Registers the hand-written constraint properties that aren't in Properties.json: Layer,
///     Skill Bonus, ID (Multiple), Cliloc (Multiple), Autoloot Match and the talisman skill-bonus
///     variants. Both the Autoloot tab and the ECV filter load their constraint list through this so
///     the two share the same set.
/// </summary>
public static class AutolootPropertyRegistration
{
    public static void LoadSpecialProperties( ObservableCollection<PropertyEntry> constraints )
    {
        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Layer,
            ConstraintType = PropertyType.Predicate,
            AllowedOperators = AutolootAllowedOperators.Equal | AutolootAllowedOperators.NotEqual,
            Predicate = ( item, entry ) =>
            {
                Layer layer = TileData.GetLayer( item.ID );

                return entry.Operator == AutolootOperator.NotPresent ||
                       AutolootHelpers.Operation( entry.Operator, entry.Value, (int) layer );
            },
            AllowedValuesEnum = typeof( Layer )
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Skill_Bonus,
            ConstraintType = PropertyType.PredicateWithValue,
            Predicate = ( item, entry ) =>
            {
                int[] clilocs = [1060451, 1060452, 1060453, 1060454, 1060455, 1072394, 1072395];

                if ( item.Properties == null )
                {
                    return false;
                }

                List<Property> properties = [.. item.Properties.Where( e => e != null && clilocs.Contains( e.Cliloc ) )];

                return MatchSkillBonus( entry, properties );
            },
            AllowedValuesEnum = typeof( SkillBonusSkills )
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.ID__Multiple_,
            ConstraintType = PropertyType.PredicateWithValue,
            UseMultipleValues = true,
            AllowedOperators = AutolootAllowedOperators.Equal | AutolootAllowedOperators.NotEqual,
            Predicate = ( item, entry ) =>
            {
                switch ( entry.Operator )
                {
                    case AutolootOperator.NotEqual:
                    case AutolootOperator.NotPresent:
                        return entry.Values == null || !entry.Values.Contains( item.ID );
                    case AutolootOperator.Equal:
                        return entry.Values != null && entry.Values.Contains( item.ID );
                    case AutolootOperator.GreaterThan:
                    case AutolootOperator.LessThan:
                    default:
                        return false;
                }
            }
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Cliloc__Multiple_,
            ConstraintType = PropertyType.PredicateWithValue,
            UseMultipleValues = true,
            AllowedOperators = AutolootAllowedOperators.Equal | AutolootAllowedOperators.NotEqual,
            Predicate = ( item, entry ) =>
            {
                if ( item.Properties == null )
                {
                    return false;
                }

                List<Property> properties = [.. item.Properties.Where( e => e != null && entry.Values != null && entry.Values.Contains( e.Cliloc ) )];

                switch ( entry.Operator )
                {
                    case AutolootOperator.NotEqual:
                    case AutolootOperator.NotPresent:
                        return !properties.Any();
                    case AutolootOperator.Equal:
                        return properties.Any();
                    case AutolootOperator.GreaterThan:
                    case AutolootOperator.LessThan:
                    default:
                        return false;
                }
            }
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Autoloot_Match,
            ConstraintType = PropertyType.PredicateWithValue,
            AllowedOperators = AutolootAllowedOperators.Equal | AutolootAllowedOperators.NotEqual,
            Predicate = ( entity, entry ) =>
            {
                AutolootEntry autoLootEntry = AutolootManager.GetInstance().GetEntries()
                    .FirstOrDefault( ale => ale.Name == entry.Additional );

                if ( autoLootEntry == null || entity is not Item item )
                {
                    return false;
                }

                IEnumerable<Item> matchItems = AutolootHelpers.AutolootFilter( [item], autoLootEntry );

                if ( entry.Operator == AutolootOperator.NotEqual )
                {
                    return !matchItems.Any();
                }

                return matchItems.Any();
            }
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Talisman_Skill_Bonus,
            ConstraintType = PropertyType.PredicateWithValue,
            Predicate = ( item, entry ) =>
            {
                if ( item.Properties == null )
                {
                    return false;
                }

                List<Property> properties = [.. item.Properties.Where( e => e != null && e.Cliloc == 1072394 )];

                return MatchSkillBonus( entry, properties );
            },
            AllowedValuesEnum = typeof( SkillBonusSkills )
        } );

        constraints.AddSorted( new PropertyEntry
        {
            Name = Strings.Talisman_Exceptional_Skill_Bonus,
            ConstraintType = PropertyType.PredicateWithValue,
            Predicate = ( item, entry ) =>
            {
                if ( item.Properties == null )
                {
                    return false;
                }

                List<Property> properties = [.. item.Properties.Where( e => e != null && e.Cliloc == 1072395 )];

                return MatchSkillBonus( entry, properties );
            },
            AllowedValuesEnum = typeof( SkillBonusSkills )
        } );
    }

    /// <summary>
    ///     Lets the additional assemblies contribute their own constraints, by invoking any
    ///     <c>public static void Initialize( ObservableCollection&lt;PropertyEntry&gt; )</c> they expose.
    ///     Call this last, after the built-in properties, so a plugin can inspect - or replace - what is
    ///     already registered.
    /// </summary>
    public static void LoadPluginProperties( ObservableCollection<PropertyEntry> constraints )
    {
        PluginAssemblies.InvokeInitialize( [typeof( ObservableCollection<PropertyEntry> )],
            [constraints] );
    }

    private static bool MatchSkillBonus( AutolootConstraintEntry entry, List<Property> properties )
    {
        if ( entry.Operator != AutolootOperator.NotPresent )
        {
            return properties.Where( property => PropertyMatches( entry, property ) )
                .Any( property => AutolootHelpers.Operation( entry.Operator, Convert.ToInt32( property.Arguments[1] ), entry.Value ) );
        }

        Property match = properties.FirstOrDefault( property => PropertyMatches( entry, property ) );

        return match == null;

        bool PropertyMatches( AutolootConstraintEntry e, Property p )
        {
            return p.Arguments != null && p.Arguments.Length >= 1 &&
                   ( e.Additional == nameof( SkillBonusSkills.Any ) ||
                     p.Arguments[0].Equals( e.Additional, System.StringComparison.CurrentCultureIgnoreCase ) ||
                     string.IsNullOrEmpty( e.Additional ) );
        }
    }
}
