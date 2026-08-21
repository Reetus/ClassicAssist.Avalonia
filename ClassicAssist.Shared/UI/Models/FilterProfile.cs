using System;
using System.Collections.ObjectModel;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models;

/// <summary>
///     A named, saved set of filter conditions for the Entity Collection Viewer. A profile is either
///     flat (<see cref="Groups" /> empty, conditions edited directly in <see cref="Conditions" /> -
///     the editor hides the group tree) or a boolean tree of groups matching WPF's
///     <c>EntityCollectionFilterEntry</c> (which a <c>FilterProfiles.json</c> written by either side
///     loads in the other; a flat profile is written as a single And group so WPF still reads it).
/// </summary>
public class FilterProfile : SetPropertyNotifyChanged
{
    public ObservableCollection<AutolootConstraintEntry> Conditions
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<EntityCollectionFilterGroup> Groups
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public Guid ID { get; set; } = Guid.NewGuid();

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    /// <summary>
    ///     Marks the first top-level group (and, recursively, each first child) as <c>IsFirst</c> - the
    ///     editor hides that node's operation selector, since the first group in a list has nothing to
    ///     combine with. Mirrors WPF's <c>EntityCollectionFilterEntry.UpdateGroupsFirstFlags</c>.
    /// </summary>
    public void UpdateGroupsFirstFlags()
    {
        for ( int i = 0; i < Groups.Count; i++ )
        {
            Groups[i].IsFirst = i == 0;
            Groups[i].UpdateChildrenFirstFlags();
        }
    }
}