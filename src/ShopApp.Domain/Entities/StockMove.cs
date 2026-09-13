using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Entities;

/// <summary>
/// APPEND-ONLY ledger of stock. Never update or delete a row.
/// On-hand for a batch = SUM(Qty) over its moves.
/// Corrections are new rows (Adjustment), so the audit trail survives.
/// </summary>
public class StockMove
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public Batch? Batch { get; set; }

    public StockMoveType MoveType { get; set; }

    /// <summary>Signed, in the item's BaseUnit. Positive = in, negative = out.</summary>
    public decimal Qty { get; set; }

    public AdjustmentReason? Reason { get; set; }
    public string? ReasonNote { get; set; }

    /// <summary>Source document, e.g. "Sale" / "Purchase", plus its Id.</summary>
    public string? RefTable { get; set; }
    public int? RefId { get; set; }

    public DateTime MovedAt { get; set; } = DateTime.Now;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
