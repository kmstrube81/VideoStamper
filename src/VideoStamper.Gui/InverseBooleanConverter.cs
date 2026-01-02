using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace VideoStamper.Gui;

public class InverseBooleanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : Avalonia.AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : Avalonia.AvaloniaProperty.UnsetValue;
}

