namespace ShopApp.UI.ViewModels;

/// <summary>
/// One plotted value. The label is whatever the bucket is called - a date, a
/// week, a month - so the chart never has to know what it is charting.
/// </summary>
public record SalesPoint(string Label, decimal Value);
