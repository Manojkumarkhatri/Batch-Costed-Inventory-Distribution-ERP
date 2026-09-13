using System.Windows;
using System.Windows.Controls;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

/// <summary>
/// The dashboard chart, opened full size with its own range and granularity.
/// Reads sales directly rather than going through a view model: it owns no
/// state beyond what is on screen, and closing it leaves nothing behind.
/// </summary>
public partial class SalesChartWindow : Window
{
    private readonly SaleService _sales;
    private string _grain = "Daily";

    public SalesChartWindow(SaleService sales, DateTime from, DateTime to)
    {
        InitializeComponent();
        _sales = sales;

        FromBox.SelectedDate = from;
        ToBox.SelectedDate = to;

        Loaded += (_, _) => Refresh();
    }

    private void Grain_Click(object sender, RoutedEventArgs e)
    {
        _grain = (string)((Button)sender).Tag;
        Refresh();
    }

    private void Range_Changed(object sender, SelectionChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (!IsLoaded) return;

        var from = FromBox.SelectedDate ?? DateTime.Today.AddMonths(-1);
        var to = ToBox.SelectedDate ?? DateTime.Today;
        if (to < from) (from, to) = (to, from);

        var rows = _sales.List(from, to).Where(r => !r.IsCancelled).ToList();
        var byDay = rows.GroupBy(r => r.Date.Date)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var points = Bucket(from, to, byDay, _grain);

        Chart.Points = points;
        TotalText.Text = "Rs " + rows.Sum(r => r.Amount).ToString("N0");
        GrainNote.Text = $"{from:dd MMM yyyy} to {to:dd MMM yyyy}  ·  {_grain.ToLower()}  ·  " +
                         $"{rows.Count} invoice{(rows.Count == 1 ? "" : "s")}";
    }

    /// <summary>
    /// Groups the day totals into whatever the granularity asks for. Quiet
    /// buckets keep their zero so the shape of the period stays honest.
    /// </summary>
    private static List<SalesPoint> Bucket(DateTime from, DateTime to,
                                           Dictionary<DateTime, decimal> byDay,
                                           string grain)
    {
        var points = new List<SalesPoint>();

        switch (grain)
        {
            case "Weekly":
                for (var d = StartOfWeek(from); d <= to; d = d.AddDays(7))
                {
                    var end = d.AddDays(6);
                    points.Add(new SalesPoint(d.ToString("d MMM"),
                        byDay.Where(k => k.Key >= d && k.Key <= end).Sum(k => k.Value)));
                }
                break;

            case "Monthly":
                for (var d = new DateTime(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1))
                    points.Add(new SalesPoint(d.ToString("MMM yy"),
                        byDay.Where(k => k.Key.Year == d.Year && k.Key.Month == d.Month)
                             .Sum(k => k.Value)));
                break;

            case "Yearly":
                for (var y = from.Year; y <= to.Year; y++)
                    points.Add(new SalesPoint(y.ToString(),
                        byDay.Where(k => k.Key.Year == y).Sum(k => k.Value)));
                break;

            default:
                for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
                    points.Add(new SalesPoint(d.ToString("d MMM"), byDay.GetValueOrDefault(d)));
                break;
        }

        return points;
    }

    private static DateTime StartOfWeek(DateTime d) =>
        d.Date.AddDays(-(int)d.DayOfWeek);
}
