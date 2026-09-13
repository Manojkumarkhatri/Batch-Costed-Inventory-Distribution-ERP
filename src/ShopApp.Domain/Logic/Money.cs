namespace ShopApp.Domain.Logic;

/// <summary>
/// All money is decimal in the domain and long paisa in SQLite.
/// SQLite has no decimal type: EF would map it to TEXT (breaks ordering)
/// or REAL (loses precision on money). Integer paisa avoids both.
/// </summary>
public static class Money
{
    public const int Scale = 100;              // 2 decimal places
    public const int QtyScale = 1000;          // 3 dp: grams, millilitres

    public static long ToPaisa(decimal amount) =>
        (long)decimal.Round(amount * Scale, 0, MidpointRounding.AwayFromZero);

    public static decimal FromPaisa(long paisa) => paisa / (decimal)Scale;

    public static long ToMilli(decimal qty) =>
        (long)decimal.Round(qty * QtyScale, 0, MidpointRounding.AwayFromZero);

    public static decimal FromMilli(long milli) => milli / (decimal)QtyScale;

    /// <summary>Round to 2dp away from zero. Bankers' rounding surprises shopkeepers.</summary>
    public static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundQty(decimal qty) =>
        decimal.Round(qty, 3, MidpointRounding.AwayFromZero);
}
