using System;
using Avalonia.Markup.Xaml;

namespace ClassicAssist.Avalonia.Misc;

/*
 * https://brianlagunas.com/a-better-way-to-data-bind-enums-in-wpf/
 */
public class EnumBindingSourceExtension : MarkupExtension
{
    public EnumBindingSourceExtension()
    {
    }

    public EnumBindingSourceExtension( Type enumType )
    {
        EnumType = enumType;
    }

    public Type EnumType
    {
        get;
        set
        {
            if ( value != field )
            {
                if ( null != value )
                {
                    Type enumType = Nullable.GetUnderlyingType( value ) ?? value;

                    if ( !enumType.IsEnum )
                    {
                        throw new ArgumentException( "Type must be for an Enum." );
                    }
                }

                field = value;
            }
        }
    }

    public override object ProvideValue( IServiceProvider serviceProvider )
    {
        if ( null == EnumType )
        {
            throw new InvalidOperationException( "The EnumType must be specified." );
        }

        Type actualEnumType = Nullable.GetUnderlyingType( EnumType ) ?? EnumType;
        Array enumValues = Enum.GetValues( actualEnumType );

        if ( actualEnumType == EnumType )
        {
            return enumValues;
        }

        Array tempArray = Array.CreateInstance( actualEnumType, enumValues.Length + 1 );
        enumValues.CopyTo( tempArray, 1 );
        return tempArray;
    }
}