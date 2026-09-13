using System.Globalization;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// Turns the grid's zero-based alternation index into a 1-based row number.
/// A serial column is the first thing anyone looks for when reading a printed
/// report back against the screen.
/// </summary>
public class RowNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (i + 1).ToString(CultureInfo.CurrentCulture) : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
