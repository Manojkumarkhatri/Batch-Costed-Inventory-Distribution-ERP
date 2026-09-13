using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// True collapses, false shows. Used to reveal the empty-state panel when a
/// DataGrid reports HasItems = false.
/// </summary>
public class NotToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
