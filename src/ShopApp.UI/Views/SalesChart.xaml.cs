using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

/// <summary>
/// Line and area chart with a hover readout.
///
/// Redrawn on every size change rather than scaled, so the stroke stays one
/// pixel and the labels stay legible whatever width it is given.
/// </summary>
public partial class SalesChart : UserControl
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IEnumerable<SalesPoint>),
            typeof(SalesChart), new PropertyMetadata(null, OnPointsChanged));

    public IEnumerable<SalesPoint>? Points
    {
        get => (IEnumerable<SalesPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private List<SalesPoint> _data = new();
    private readonly List<(Point At, SalesPoint P)> _plotted = new();

    // Room for the axis labels. Everything else is the plot area.
    private const double PadLeft = 54, PadRight = 14, PadTop = 14, PadBottom = 28;

    public SalesChart()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Draw();
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => Tip.Visibility = Visibility.Collapsed;
    }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SalesChart)d;

        if (e.OldValue is INotifyCollectionChanged old)
            old.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged fresh)
            fresh.CollectionChanged += chart.OnCollectionChanged;

        chart.Draw();
    }

    private void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e) => Draw();

    private static SolidColorBrush Res(string key) =>
        (SolidColorBrush)Application.Current.FindResource(key);

    private void Draw()
    {
        Plot.Children.Clear();
        _plotted.Clear();

        _data = Points?.ToList() ?? new List<SalesPoint>();
        double w = ActualWidth, h = ActualHeight;
        if (_data.Count == 0 || w < 60 || h < 60) return;

        var plotW = w - PadLeft - PadRight;
        var plotH = h - PadTop - PadBottom;
        if (plotW <= 0 || plotH <= 0) return;

        // A flat run of zeroes still needs a scale, or every point lands on
        // the axis and the chart looks broken rather than empty.
        var max = _data.Max(p => p.Value);
        var scale = max <= 0 ? 1m : NiceCeiling(max);

        var line = Res("Line");
        var faint = Res("Faint");
        var accent = Res("Accent");

        // ---- gridlines and the value axis
        for (var i = 0; i <= 4; i++)
        {
            var y = PadTop + plotH - plotH * i / 4.0;

            Plot.Children.Add(new Line
            {
                X1 = PadLeft, X2 = PadLeft + plotW, Y1 = y, Y2 = y,
                Stroke = line, StrokeThickness = 1, SnapsToDevicePixels = true
            });

            var label = new TextBlock
            {
                Text = Short(scale * i / 4m),
                FontSize = 10,
                Foreground = faint,
                FontFamily = (FontFamily)Application.Current.FindResource("NumFont")
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, PadLeft - label.DesiredSize.Width - 8);
            Canvas.SetTop(label, y - label.DesiredSize.Height / 2);
            Plot.Children.Add(label);
        }

        // ---- the points
        var pts = new PointCollection();
        for (var i = 0; i < _data.Count; i++)
        {
            var x = _data.Count == 1
                ? PadLeft + plotW / 2
                : PadLeft + plotW * i / (_data.Count - 1);
            var y = PadTop + plotH - plotH * (double)(_data[i].Value / scale);

            pts.Add(new Point(x, y));
            _plotted.Add((new Point(x, y), _data[i]));
        }

        // ---- filled area under the line
        var area = new PointCollection(pts) { };
        area.Insert(0, new Point(pts[0].X, PadTop + plotH));
        area.Add(new Point(pts[^1].X, PadTop + plotH));

        Plot.Children.Add(new Polygon
        {
            Points = area,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0x55, 0x2F, 0x5B, 0xEA),
                Color.FromArgb(0x00, 0x2F, 0x5B, 0xEA),
                new Point(0, 0), new Point(0, 1))
        });

        Plot.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = accent,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round
        });

        // ---- a dot on each point, but only when they are far enough apart
        //      to be told apart. On a year of daily figures they merge into
        //      a smear and add nothing.
        if (_data.Count <= 40)
            foreach (var p in pts)
            {
                var dot = new Ellipse
                {
                    Width = 5, Height = 5,
                    Fill = Brushes.White, Stroke = accent, StrokeThickness = 1.6
                };
                Canvas.SetLeft(dot, p.X - 2.5);
                Canvas.SetTop(dot, p.Y - 2.5);
                Plot.Children.Add(dot);
            }

        // ---- the date axis, thinned to whatever fits
        var every = Math.Max(1, (int)Math.Ceiling(_data.Count / (plotW / 74)));
        for (var i = 0; i < _data.Count; i += every)
        {
            var label = new TextBlock
            {
                Text = _data[i].Label, FontSize = 10, Foreground = faint
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, pts[i].X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, PadTop + plotH + 8);
            Plot.Children.Add(label);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_plotted.Count == 0) { Tip.Visibility = Visibility.Collapsed; return; }

        var here = e.GetPosition(this);

        var nearest = _plotted[0];
        var best = double.MaxValue;
        foreach (var p in _plotted)
        {
            var d = Math.Abs(p.At.X - here.X);
            if (d < best) { best = d; nearest = p; }
        }

        // Only speak when the cursor is actually near a point.
        if (best > 30) { Tip.Visibility = Visibility.Collapsed; return; }

        TipLabel.Text = nearest.P.Label;
        TipValue.Text = "Rs " + nearest.P.Value.ToString("N0", CultureInfo.CurrentCulture);

        Tip.Visibility = Visibility.Visible;
        Tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var left = Math.Min(Math.Max(0, nearest.At.X - Tip.DesiredSize.Width / 2),
                            ActualWidth - Tip.DesiredSize.Width);
        Tip.Margin = new Thickness(left, Math.Max(0, nearest.At.Y - Tip.DesiredSize.Height - 12), 0, 0);
    }

    /// <summary>
    /// Rounds the axis up to something a person would choose - 1, 2 or 5 times
    /// a power of ten - so the gridlines land on readable numbers instead of
    /// 19,500 divided by four.
    /// </summary>
    private static decimal NiceCeiling(decimal value)
    {
        if (value <= 0) return 1m;

        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)value)));
        var normalised = value / magnitude;

        var step = normalised <= 1m ? 1m
                 : normalised <= 2m ? 2m
                 : normalised <= 5m ? 5m
                 : 10m;

        return step * magnitude;
    }

    /// <summary>1,200,000 becomes 1.2m. Axis labels have no room for zeroes.</summary>
    private static string Short(decimal v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000).ToString("0.#") + "m",
        >= 1_000 => (v / 1_000).ToString("0.#") + "k",
        _ => v.ToString("0")
    };
}
