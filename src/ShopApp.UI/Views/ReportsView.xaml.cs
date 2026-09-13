using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ShopApp.Domain.Logic;
using ShopApp.Services;
using ShopApp.UI.Converters;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class ReportsView : UserControl
{
    private readonly ReportsViewModel _vm;

    public ReportsView(ReportsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;

        // Every report has different columns, so the grid is rebuilt each run
        // rather than being declared in XAML.
        vm.OnResultReady = BuildGrid;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.LoadLookups();
        };
    }

    /// <summary>
    /// Double-click opens the report across the whole window by folding the
    /// list away. Double-click anywhere in the list again to bring it back -
    /// there is also a button, since a hidden gesture nobody finds is no
    /// feature at all.
    /// </summary>
    private void ReportItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        _vm.ToggleListCommand.Execute(null);
        e.Handled = true;
    }

    private void ReportRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: ReportDefinition def })
            _vm.SelectedReport = def;
    }

    private void BuildGrid(ReportResult report)
    {
        ReportGrid.Columns.Clear();

        // A serial column, before anything the report itself defines. It is
        // what someone uses to find their place again when reading a printed
        // copy back against the screen.
        ReportGrid.AlternationCount = int.MaxValue;
        ReportGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Width = new DataGridLength(44, DataGridLengthUnitType.Pixel),
            Binding = new Binding("(ItemsControl.AlternationIndex)")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(DataGridRow)
                },
                Converter = new RowNumberConverter()
            },
            HeaderStyle = (Style)Application.Current.FindResource("NumberColumnHeader"),
            ElementStyle = (Style)Application.Current.FindResource("RowNumberCell")
        });

        foreach (var col in report.Columns)
        {
            var binding = new Binding($"[{col.Field}]");

            switch (col.Type)
            {
                case ReportColumnType.Money:
                    binding.Converter = new ReportValueConverter(ReportColumnType.Money); break;
                case ReportColumnType.Quantity:
                    binding.Converter = new ReportValueConverter(ReportColumnType.Quantity); break;
                case ReportColumnType.Number:
                    binding.Converter = new ReportValueConverter(ReportColumnType.Number); break;
                case ReportColumnType.Percent:
                    binding.Converter = new ReportValueConverter(ReportColumnType.Percent); break;
                case ReportColumnType.Date:
                    binding.StringFormat = "dd-MM-yyyy"; break;
            }

            var column = new DataGridTextColumn
            {
                Header = col.Header.ToUpperInvariant(),
                Binding = binding,
                Width = new DataGridLength(col.Width, DataGridLengthUnitType.Star)
            };

            // Header AND cell are aligned together. Aligning only the cell is
            // what made every figure look like it belonged to the next column.
            if (col.Type is ReportColumnType.Money or ReportColumnType.Quantity
                         or ReportColumnType.Number or ReportColumnType.Percent)
            {
                column.HeaderStyle =
                    (Style)Application.Current.FindResource("NumberColumnHeader");
                column.ElementStyle =
                    (Style)Application.Current.FindResource("NumberCell");
            }
            else
            {
                column.ElementStyle = (Style)Application.Current.FindResource(
                    col.Type == ReportColumnType.Date ? "DateCell" : "TextCell");
            }

            ReportGrid.Columns.Add(column);
        }

        // Reports that set a Section on their rows get real group bands.
        var grouped = report.Rows.Any(r => r.Section is not null);

        if (grouped)
        {
            var view = new CollectionViewSource { Source = report.Rows };
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReportRow.Section)));
            ReportGrid.ItemsSource = view.View;
        }
        else
        {
            ReportGrid.ItemsSource = report.Rows;
        }

        ReportGrid.GroupStyle.Clear();
        if (grouped)
            ReportGrid.GroupStyle.Add((GroupStyle)FindResource("ReportGroupStyle"));
    }
}

/// <summary>Formats report values consistently between the grid and the exports.</summary>
public class ReportValueConverter : IValueConverter
{
    private readonly ReportColumnType _type;
    public ReportValueConverter(ReportColumnType type) => _type = type;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // A cell with nothing in it stays empty. Printing 0.00 on a section
        // heading, or on a payment that has no quantity, says something the
        // row does not mean.
        if (value is null) return "";

        var d = value switch
        {
            decimal m => m,
            int i => i,
            double db => (decimal)db,
            _ => 0m
        };

        return _type switch
        {
            ReportColumnType.Money or ReportColumnType.Number => Plain(d),
            ReportColumnType.Quantity => Plain(d),
            ReportColumnType.Percent => $"{d:0.##}%",
            _ => value?.ToString() ?? ""
        };
    }

    /// <summary>
    /// Whole figures lose the ".00". A column of 50,000 and 97,500 reads far
    /// faster without two zeroes on every line, and he trades in whole rupees.
    ///
    /// Anything that is NOT whole keeps its decimals - showing 50,000 where
    /// the figure is 50,000.50 would be a lie, and a total that does not add
    /// up is worse than a longer number. Invoices and the exports are
    /// untouched: those are documents, and they keep full precision.
    /// </summary>
    private static string Plain(decimal d) =>
        d == decimal.Truncate(d) ? d.ToString("N0") : d.ToString("N2");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
