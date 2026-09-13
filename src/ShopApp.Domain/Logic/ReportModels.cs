namespace ShopApp.Domain.Logic;

public enum ReportColumnType { Text, Number, Money, Quantity, Date, Percent }

public record ReportColumn(
    string Header,
    string Field,
    ReportColumnType Type = ReportColumnType.Text,
    bool ShowTotal = false,
    double Width = 1.0);

public class ReportRow
{
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string field]
    {
        get => _values.TryGetValue(field, out var v) ? v : null;
        set => _values[field] = value;
    }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public decimal Num(string field) => this[field] switch
    {
        decimal d => d,
        int i => i,
        double db => (decimal)db,
        _ => 0m
    };

    /// <summary>Marks subtotal or grand-total rows so the grid can bold them.</summary>
    public bool IsSummaryRow { get; set; }

    /// <summary>
    /// Which block of the report this row belongs to - "SALES", "PURCHASES".
    /// Null on reports that have no blocks.
    ///
    /// A heading used to be a row of its own, which put the word "SALES" in
    /// the middle of a table with eight empty cells beside it. As a group key
    /// the grid can draw a proper full-width band instead, which is what the
    /// design does.
    /// </summary>
    public string? Section { get; set; }

    /// <summary>
    /// How many real entries the section holds. The grid's own ItemCount
    /// counts every row in the group, and the subtotal is a row - so a
    /// section with one sale in it reported "2 entries".
    /// </summary>
    public int SectionCount { get; set; }
}

/// <summary>
/// Every report returns this same shape, so one screen and one exporter serve
/// all of them. Adding a report means adding a query, not a new view.
/// </summary>
public record ReportResult(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<ReportRow> Rows,
    IReadOnlyDictionary<string, decimal> Totals,
    string? Subtitle = null,
    IReadOnlyList<(string Label, string Value)>? Highlights = null)
{
    public static ReportResult Empty(string title, string? note = null) =>
        new(title, Array.Empty<ReportColumn>(), Array.Empty<ReportRow>(),
            new Dictionary<string, decimal>(), note);

    public bool HasRows => Rows.Count > 0;
}

/// <summary>What the user picked on the filter bar.</summary>
public record ReportFilter(
    DateTime From,
    DateTime To,
    int? PartyId = null,
    int? ItemId = null,
    string? PartyGroup = null,
    Enums.ItemCategory? Category = null)
{
    public static ReportFilter ThisMonth()
    {
        var today = DateTime.Today;
        return new ReportFilter(new DateTime(today.Year, today.Month, 1), today);
    }

    public bool Covers(DateTime date) => date.Date >= From.Date && date.Date <= To.Date;

    public string RangeText => $"{From:dd-MM-yyyy} to {To:dd-MM-yyyy}";
}
