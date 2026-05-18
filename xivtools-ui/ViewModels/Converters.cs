using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace XivToolsUI.ViewModels;

public class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#0D2B1A"))
            : new SolidColorBrush(Color.Parse("#2B0D0D"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public class BoolToStatusConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true ? "● Ready" : "● Not initialized";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is true
            ? new SolidColorBrush(Color.Parse("#10B981"))
            : new SolidColorBrush(Color.Parse("#EF4444"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

// Returns one of two brushes based on whether string == parameter
public class ActivePageConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var active = value as string;
        var key    = p as string;
        return active == key
            ? new SolidColorBrush(Color.FromArgb(40, 124, 58, 237))   // active tint
            : new SolidColorBrush(Colors.Transparent);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public class ActivePageBorderConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var active = value as string;
        var key    = p as string;
        return active == key
            ? new SolidColorBrush(Color.Parse("#7C3AED"))
            : new SolidColorBrush(Colors.Transparent);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

// Bool → one of two strings, passed as "TrueText|FalseText" parameter
public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var parts = (p as string)?.Split('|') ?? Array.Empty<string>();
        return value is true ? (parts.Length > 0 ? parts[0] : "Yes") : (parts.Length > 1 ? parts[1] : "No");
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

// Bool → one of two colors
public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var parts = (p as string)?.Split('|') ?? Array.Empty<string>();
        var hex   = value is true ? (parts.Length > 0 ? parts[0] : "#10B981") : (parts.Length > 1 ? parts[1] : "#EF4444");
        return new SolidColorBrush(Color.Parse(hex));
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}
