using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Avalonia.Views;

public partial class SkillsTabControl : UserControl
{
    private readonly Dictionary<string, ListSortDirection> _sortDirections =
        [];

    private bool _applyingSort;

    public SkillsTabControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load( this );
    }

    private void OnDataGridLoaded( object sender, RoutedEventArgs e )
    {
        if ( sender is DataGrid dataGrid )
        {
            ApplyPersistedSort( dataGrid );
        }
    }

    private void OnDataGridSorting( object sender, DataGridColumnEventArgs e )
    {
        if ( _applyingSort || DataContext is not SkillsTabViewModel vm )
        {
            return;
        }

        string member = e.Column.SortMemberPath;

        if ( string.IsNullOrEmpty( member ) )
        {
            return;
        }

        SkillSortBy? sortBy = MapSortMember( member );

        // A SortMemberPath that no longer maps (renamed in SkillsTabControl.axaml) leaves the
        // grid's own default sort in place but persists nothing, rather than silently recording
        // SkillSortBy.Name for the wrong column.
        if ( sortBy == null )
        {
            return;
        }

        if ( _sortDirections.TryGetValue( member, out ListSortDirection current ) )
        {
            switch ( current )
            {
                case ListSortDirection.Ascending:
                    _sortDirections[member] = ListSortDirection.Descending;
                    vm.SetSort( sortBy.Value, ListSortDirection.Descending );

                    return;
                case ListSortDirection.Descending:
                    _sortDirections.Remove( member );
                    vm.ClearSort();

                    return;
            }
        }

        _sortDirections.Clear();
        _sortDirections[member] = ListSortDirection.Ascending;
        vm.SetSort( sortBy.Value, ListSortDirection.Ascending );
    }

    private void ApplyPersistedSort( DataGrid dataGrid )
    {
        if ( DataContext is not SkillsTabViewModel vm || vm.SortInfo == null )
        {
            return;
        }

        string sortMember = MapSortMember( vm.SortInfo.SortBy );

        // A persisted SkillSortBy with no matching column (enum value added or SortMemberPath
        // renamed since it was saved) must not match a nameless column - skip restoring the sort.
        if ( sortMember == null )
        {
            return;
        }

        DataGridColumn column = dataGrid.Columns.FirstOrDefault( c => c.SortMemberPath == sortMember );

        if ( column == null )
        {
            return;
        }

        _applyingSort = true;

        try
        {
            column.Sort( vm.SortInfo.Direction );

            _sortDirections.Clear();
            _sortDirections[column.SortMemberPath] = vm.SortInfo.Direction;
        }
        finally
        {
            _applyingSort = false;
        }
    }

    private static string MapSortMember( SkillSortBy sortBy )
    {
        switch ( sortBy )
        {
            // Skill.Name rather than Skill: SkillEntry.Skill is a struct with no IComparable, so
            // sorting on it resolves Comparer<Skill>.Default and throws on the first comparison.
            case SkillSortBy.Name:
                return "Skill.Name";
            case SkillSortBy.Value:
                return "Value";
            case SkillSortBy.Base:
                return "Base";
            case SkillSortBy.Delta:
                return "Delta";
            case SkillSortBy.Cap:
                return "Cap";
            case SkillSortBy.LockStatus:
                return "LockStatus";
        }

        return null;
    }

    /// <summary>
    ///     Maps a column's <c>SortMemberPath</c> (as declared in SkillsTabControl.axaml) to the
    ///     persisted <see cref="SkillSortBy" />; returns null for a path that no longer maps so the
    ///     caller can skip persisting rather than recording the wrong sort.
    /// </summary>
    private static SkillSortBy? MapSortMember( string member )
    {
        switch ( member )
        {
            case "Skill.Name":
                return SkillSortBy.Name;
            case "Value":
                return SkillSortBy.Value;
            case "Base":
                return SkillSortBy.Base;
            case "Delta":
                return SkillSortBy.Delta;
            case "Cap":
                return SkillSortBy.Cap;
            case "LockStatus":
                return SkillSortBy.LockStatus;
        }

        return null;
    }
}
