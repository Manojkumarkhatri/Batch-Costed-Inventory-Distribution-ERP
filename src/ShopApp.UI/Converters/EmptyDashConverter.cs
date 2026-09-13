using System.Globalization;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// Renders an empty value as an em dash.
///
/// A blank cell reads as "not loaded yet"; a nought reads as a real figure of
/// zero. Neither is what a missing email address or a payment's quantity
/// means. The dash says "nothing here, and that is correct".
/// </summary>
public class EmptyDashConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "—";
        if (value is string s) return string.IsNullOrWhiteSpace(s) ? "—" : s;
        if (value is decimal d && d == 0m) return "—";
        return value;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
