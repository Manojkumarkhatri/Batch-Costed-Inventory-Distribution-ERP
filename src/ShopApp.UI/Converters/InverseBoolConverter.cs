using System.Globalization;
using System.Windows.Data;

namespace ShopApp.UI.Converters;

/// <summary>
/// Lets the "To Pay" radio bind to the inverse of OpeningIsReceivable, so the
/// pair behaves as one setting rather than two independent flags.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}
