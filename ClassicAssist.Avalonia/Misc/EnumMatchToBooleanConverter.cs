using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ClassicAssist.Avalonia.Misc
{
    /// <summary>Drives a checkable MenuItem's IsChecked from an enum property matching ConverterParameter.</summary>
    public class EnumMatchToBooleanConverter : IValueConverter
    {
        public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
        {
            return value != null && parameter != null && value.Equals( parameter );
        }

        public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
        {
            return value is bool isChecked && isChecked ? parameter : AvaloniaProperty.UnsetValue;
        }
    }
}
