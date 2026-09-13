using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// Shows placeholder text while a picker has nothing chosen. An empty grey
/// combo says nothing about what it filters; "All parties" does.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
