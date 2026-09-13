using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ShopApp.Domain.Logic;

namespace ShopApp.Data.Converters;

/// <summary>
/// Money is stored as INTEGER paisa, quantity as INTEGER milli-units.
/// SQLite has no decimal type. EF's default mapping is TEXT (breaks ORDER BY
/// and SUM) or REAL (loses precision on money). Integers avoid both.
///
/// Consequence: EF cannot translate SUM/AVG over these columns into SQL.
/// Report aggregation therefore filters in SQL, then aggregates in memory
/// (see ReportService). At this business's volume that is comfortably fast.
/// </summary>
public class MoneyConverter : ValueConverter<decimal, long>
{
    public MoneyConverter()
        : base(v => Money.ToPaisa(v), v => Money.FromPaisa(v)) { }
}

public class NullableMoneyConverter : ValueConverter<decimal?, long?>
{
    public NullableMoneyConverter()
        : base(v => v == null ? null : Money.ToPaisa(v.Value),
               v => v == null ? null : Money.FromPaisa(v.Value)) { }
}

public class QtyConverter : ValueConverter<decimal, long>
{
    public QtyConverter()
        : base(v => Money.ToMilli(v), v => Money.FromMilli(v)) { }
}

public class NullableQtyConverter : ValueConverter<decimal?, long?>
{
    public NullableQtyConverter()
        : base(v => v == null ? null : Money.ToMilli(v.Value),
               v => v == null ? null : Money.FromMilli(v.Value)) { }
}
