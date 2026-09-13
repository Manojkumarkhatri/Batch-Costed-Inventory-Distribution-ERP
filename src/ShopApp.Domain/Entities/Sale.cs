namespace ShopApp.Domain.Entities;

public class Sale
{
    public int Id { get; set; }

    /// <summary>
    /// The number PRINTED on the customer's bill. Plain sequential ("7") to
    /// match the invoice he approved.
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// Internal reference he sees but the customer never does. Never printed.
    /// Always a gapless count of every sale ever recorded, including cancelled
    /// ones, so it works as an audit sequence even if a printed number is
    /// skipped or reused.
    ///
    /// He asked for this without giving a reason. The usual reason is that a
    /// visible sequential number tells every customer how many bills he has
    /// issued, which reveals his trading volume - a private series avoids that.
    /// Worth confirming before relying on this interpretation.
    /// </summary>
    public int InternalRefNo { get; set; }

    public int CustomerId { get; set; }
    public Party? Customer { get; set; }

    public DateTime Date { get; set; } = DateTime.Today;

    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    public string? Notes { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<SaleLine> Lines { get; set; } = new();
}

public class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>Which batch this came out of. Drives exact margin and FEFO.</summary>
    public int BatchId { get; set; }
    public Batch? Batch { get; set; }

    public decimal? PackQty { get; set; }
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Snapshot of batch cost at sale time, so history never shifts.</summary>
    public decimal CostAtSale { get; set; }
}
