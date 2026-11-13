using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FCPModUpdater.Gui.Controls;

/// <summary>Looks up an icon geometry in the application resources by key.</summary>
public sealed class GeometryKeyConverter : IValueConverter
{
    public static readonly GeometryKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is { } app && app.TryGetResource(key, null, out var resource)
            ? resource as Geometry
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
