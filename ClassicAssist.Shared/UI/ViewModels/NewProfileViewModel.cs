using System.IO;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.ViewModels;

namespace ClassicAssist.Shared.UI.ViewModels;

public enum NewProfileOption
{
    Blank,
    Duplicate
}

public class NewProfileViewModel : BaseViewModel
{
    // Avalonia only
    public ICommand ChangeOptionCommand => field ??=
            new RelayCommand( o => ChangeOption( (NewProfileOption) o ), o => true );

    public string FileName { get; set; }

    public string Name
    {
        get;
        set => SetProperty( ref field, value );
    }

    public ICommand OkCommand => field ??= new RelayCommand( Ok, o => !string.IsNullOrEmpty( Name ) );

    public NewProfileOption Option
    {
        get;
        set => SetProperty( ref field, value );
    } = NewProfileOption.Duplicate;

    private void Ok( object obj )
    {
        string profileName = Name?.Trim();

        bool valid = profileName?.IndexOfAny( Path.GetInvalidFileNameChars() ) == -1;

        if ( valid )
        {
            FileName = $"{profileName}.json";

            if ( Option == NewProfileOption.Duplicate )
            {
                Options options = Options.CurrentOptions;
                options.Name = $"{profileName}.json";
                Options.Save( options );
            }
            else
            {
                Options.ClearOptions();
                Options options = new() { Name = $"{profileName}.json" };
                Options.CurrentOptions = options;
                Options.Load( options.Name, options );
                Options.Save( options );
            }
        }
        else
        {
            Engine.MessageBoxProvider.Show( Strings.Profile_name_contains_illegal_characters_ );
        }
    }

    private void ChangeOption( NewProfileOption obj )
    {
        Option = obj;
    }
}