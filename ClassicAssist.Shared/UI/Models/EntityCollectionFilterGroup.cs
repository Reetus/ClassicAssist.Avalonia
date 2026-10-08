using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Shared.Resources;
using ClassicAssist.Shared.UI;

namespace ClassicAssist.UI.Models;

/// <summary>
///     A node in a filter profile's boolean tree. A group is a branch when it has
///     <see cref="Children" /> (its own <see cref="Items" /> are ignored - they're hidden in the
///     editor) and a leaf when it doesn't. Operation describes how the group combines with the one
///     before it at its level; the first group in any list has no preceding node, so its Operation is
///     ignored when evaluating. Mirrors WPF's <c>EntityCollectionFilterGroup</c>, minus the
///     draggable-tree plumbing.
/// </summary>
public class EntityCollectionFilterGroup : SetPropertyNotifyChanged
{
    public EntityCollectionFilterGroup()
    {
        Children.CollectionChanged += OnChildrenCollectionChanged;
        Items.CollectionChanged += OnItemsCollectionChanged;
    }

    public ObservableCollection<EntityCollectionFilterGroup> Children
    {
        get;
        set
        {
            field?.CollectionChanged -= OnChildrenCollectionChanged;

            field = value ?? [];

            OnPropertyChanged( nameof( Children ) );
            OnPropertyChanged( nameof( HasChildren ) );
            OnPropertyChanged( nameof( Name ) );

            field?.CollectionChanged += OnChildrenCollectionChanged;

            UpdateChildrenFirstFlags();
        }
    } = [];

    public bool IsFirst
    {
        get;
        set => SetProperty( ref field, value );
    } = true;

    public bool HasChildren => Children.Count > 0;

    public ObservableCollection<AutolootConstraintEntry> Items
    {
        get;
        set
        {
            field?.CollectionChanged -= OnItemsCollectionChanged;

            SetProperty( ref field, value );

            field?.CollectionChanged += OnItemsCollectionChanged;

            OnPropertyChanged( nameof( Name ) );
        }
    } = [];

    public string Name
    {
        get
        {
            if ( !string.IsNullOrEmpty( field ) )
            {
                return field;
            }

            return HasChildren
                ? string.Format( Strings.Filter_Group_Subgroups, Children.Count )
                : string.Format( Strings.Filter_Group_Filters, Items.Count );
        }
        set => SetProperty( ref field, value );
    }

    public BooleanOperation Operation
    {
        get;
        set => SetProperty( ref field, value );
    }

    private void OnChildrenCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        OnPropertyChanged( nameof( HasChildren ) );
        OnPropertyChanged( nameof( Name ) );
        UpdateChildrenFirstFlags();
    }

    private void OnItemsCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
    {
        if ( !HasChildren )
        {
            OnPropertyChanged( nameof( Name ) );
        }
    }

    public void UpdateChildrenFirstFlags()
    {
        for ( int i = 0; i < Children.Count; i++ )
        {
            EntityCollectionFilterGroup child = Children[i];
            child.IsFirst = i == 0;
            child.UpdateChildrenFirstFlags();
        }
    }
}

/// <summary>How a filter group combines with the group before it at its level. First groups in a list
/// have nothing to combine with, so their operation is ignored when evaluating.</summary>
public enum BooleanOperation
{
    [Description( "And (&&)" )]
    And,

    [Description( "Or (||)" )]
    Or,

    [Description( "Not (!)" )]
    Not
}