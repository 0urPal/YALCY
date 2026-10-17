using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace YALCY.ViewModels;

// NumericUpDown.Value can be empty while the user edits, but the settings it binds to are whole numbers.
// Writing "empty" back would fail with a validation error, so skip it and keep the value in use.
public class NumericBoxConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is IConvertible and not string and not bool
            ? System.Convert.ToDecimal(value, culture)
            : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal number)
        {
            return BindingOperations.DoNothing;
        }

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return System.Convert.ChangeType(Math.Round(number), type, culture);
    }
}
