#region License

// Copyright (C) 2024 Reetus
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ClassicAssist.Data.Autoloot;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;
using Newtonsoft.Json;

namespace ClassicAssist.Shared.UI.ViewModels.Autoloot;

public class CSVImportViewModel : BaseViewModel
{
    private readonly string[] _operators = ["==", "!=", ">=", "<=", "X"];
    private readonly string _propertiesFile =
        Path.Combine( Engine.StartupPath ?? Environment.CurrentDirectory, "Data", "Properties.json" );

    public CSVImportViewModel()
    {
        LoadProperties();
    }

    public ObservableCollection<PropertyEntry> Constraints
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public ObservableCollection<AutolootEntry> Entries
    {
        get;
        set => SetProperty( ref field, value );
    } = [];

    public bool IgnoreDuplicateEntries
    {
        get;
        set => SetProperty( ref field, value );
    }

    public bool Import { get; set; }

    public AutolootEntry SelectedEntry
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand SelectFileCommand => field ??= new RelayCommandAsync( SelectFile, o => true );

    public ICommand SetImportCommand => field ??= new RelayCommand( SetImport, o => true );

    private async Task SelectFile( object obj )
    {
        string fileName = await Engine.UIInvoker.ShowOpenFileDialogAsync( Strings.CSV_Import, "CSV files",
            ["*.csv"] );

        if ( string.IsNullOrEmpty( fileName ) )
        {
            return;
        }

        LoadFile( fileName );
    }

    private void SetImport( object obj )
    {
        Import = true;
    }

    private void LoadFile( string fileName )
    {
        try
        {
            using StreamReader reader = new( fileName );
            CsvReader csv = new( reader );
            csv.ReadHeader();

            while ( csv.Read() )
            {
                if ( !csv.TryGetField( "ID", out string idString ) )
                {
                    continue;
                }

                try
                {
                    int id = ParseId( idString );

                    string name = $"0x{id:x}";

                    if ( csv.TryGetField( "Name", out string nameString ) )
                    {
                        name = nameString;
                    }

                    AutolootEntry autolootEntry = new()
                    {
                        ID = id,
                        Autoloot = true,
                        Enabled = true,
                        Priority = AutolootPriority.Normal,
                        Name = name,
                        Constraints = [],
                        Rehue = false
                    };

                    List<string> columns = [.. csv.HeaderRecord.Where( value => value.StartsWith( "Property" ) )];

                    if ( columns.Any() )
                    {
                        foreach ( string column in columns )
                        {
                            if ( !csv.TryGetField( column, out string fieldValue ) )
                            {
                                continue;
                            }

                            if ( string.IsNullOrEmpty( fieldValue ) )
                            {
                                continue;
                            }

                            PropertyEntry entry = Constraints.FirstOrDefault( e =>
                                fieldValue.Contains( e.ShortName ) );

                            if ( entry == null )
                            {
                                continue;
                            }

                            AutolootOperator operation = AutolootOperator.Equal;

                            string remaining = fieldValue[entry.ShortName.Length..];

                            foreach ( string @operator in _operators )
                            {
                                if ( !remaining.StartsWith( @operator ) )
                                {
                                    continue;
                                }

                                operation = GetOperator( @operator );
                                remaining = remaining[@operator.Length..];

                                break;
                            }

                            int value = remaining.Length > 0
                                ? Convert.ToInt32( remaining.Trim(), CultureInfo.InvariantCulture )
                                : 0;

                            autolootEntry.Constraints.Add( new AutolootConstraintEntry
                            {
                                Property = entry,
                                Operator = operation,
                                Value = value
                            } );
                        }
                    }

                    Entries.Add( autolootEntry );
                }
                catch ( Exception )
                {
                    // We tried
                }
            }
        }
        catch ( Exception )
        {
            Engine.MessageBoxProvider.Show( Strings.Error_loading_file__ensure_it_isn_t_currently_in_use,
                Strings.Error, MessageBoxButtons.OK, MessageBoxImage.Error );
        }
    }

    private static int ParseId( string idString )
    {
        string trimmed = idString.Trim();

        if ( trimmed.StartsWith( "0x", StringComparison.CurrentCultureIgnoreCase ) )
        {
            return Convert.ToInt32( trimmed[2..], 16 );
        }

        return Convert.ToInt32( trimmed, CultureInfo.InvariantCulture );
    }

    private void LoadProperties()
    {
        if ( !File.Exists( _propertiesFile ) )
        {
            return;
        }

        JsonSerializer serializer = new();
        List<PropertyEntry> list = [];

        using ( StreamReader sr = new( _propertiesFile ) )
        {
            using JsonTextReader reader = new( sr );
            PropertyEntry[] constraints = serializer.Deserialize<PropertyEntry[]>( reader );

            if ( constraints == null )
            {
                return;
            }

            list.AddRange( constraints );
        }

        foreach ( PropertyEntry entry in list.Where( e => !string.IsNullOrEmpty( e.ShortName ) )
                     .OrderByDescending( e => e.ShortName.Length ) )
        {
            Constraints.Add( entry );
        }
    }

    private static AutolootOperator GetOperator( string @operator )
    {
        switch ( @operator )
        {
            case "==":
                return AutolootOperator.Equal;
            case "!=":
                return AutolootOperator.NotEqual;
            case ">=":
                return AutolootOperator.GreaterThan;
            case "<=":
                return AutolootOperator.LessThan;
            case "X":
                return AutolootOperator.NotPresent;
        }

        return AutolootOperator.Equal;
    }
}
