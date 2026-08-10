using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassicAssist.Launcher.Models;
using ClassicAssist.Launcher.ViewModels;

namespace ClassicAssist.Launcher.Views;

public partial class ShardsWindow : Window
{
    public ShardsWindow()
    {
        InitializeComponent();
        SetupColumnComparers();

        if ( DataContext is ShardsViewModel vm )
        {
            vm.CloseRequested += Close;
        }
    }

    // Columns are declared in ShardsWindow.axaml in this exact order: Shard, Address, Port,
    // Encryption, Players, Ping. DataGridColumn isn't a StyledElement, so it can't carry an
    // x:Name for field generation - indexing by position (matching ShardListViewComparer's old
    // per-column switch) is the simplest reliable way to wire these up.
    private void SetupColumnComparers()
    {
        IReadOnlyList<DataGridColumn> columns = ShardsGrid.Columns;

        columns[0].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.Ordinal ) );
        columns[1].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => string.Compare( a.Address, b.Address, StringComparison.Ordinal ) );
        columns[2].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => a.Port.CompareTo( b.Port ) );
        columns[3].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => a.Encryption.CompareTo( b.Encryption ) );
        columns[4].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => ParseOrZero( a.Status ).CompareTo( ParseOrZero( b.Status ) ) );
        columns[5].CustomSortComparer = Comparer<ShardEntry>.Create( ( a, b ) => ParseOrZero( a.Ping ).CompareTo( ParseOrZero( b.Ping ) ) );
    }

    private static int ParseOrZero( string value )
    {
        return !string.IsNullOrEmpty( value ) && value != "-" && int.TryParse( value, out int result ) ? result : 0;
    }

    private void OnOpenGitHubIssue( object sender, RoutedEventArgs e )
    {
        Process.Start( new ProcessStartInfo { FileName = "https://github.com/Reetus/ClassicAssist-Shards/issues", UseShellExecute = true } );
    }
}
