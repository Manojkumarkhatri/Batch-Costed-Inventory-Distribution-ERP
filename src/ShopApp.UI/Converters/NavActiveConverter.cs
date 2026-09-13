using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// True when two bound values refer to the same thing. Used twice: to light the
/// rail button whose Tag matches the open section, and to check the radio of
/// whichever report is currently selected. Strings compare case-insensitively;
/// everything else uses Equals, which is value equality for the record types.
/// </summary>
public class NavActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;

        var a = values[0];
        var b = values[1];

        if (a is null || b is null) return false;
        if (ReferenceEquals(a, DependencyProperty.UnsetValue)) return false;
        if (ReferenceEquals(b, DependencyProperty.UnsetValue)) return false;

        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);

        return a.Equals(b);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
