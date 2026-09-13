namespace ShopApp.Domain.Entities;

public class Purchase
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Party? Supplier { get; set; }

    /// <summary>The supplier's own bill number, as printed on his paper.</summary>
    public string? SupplierBillNo { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;

    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }

    /// <summary>Freight, labour, cold storage. Apportioned into batch CostPrice.</summary>
    public decimal OtherCharges { get; set; }
    public decimal Total { get; set; }

    public string? Notes { get; set; }
    public bool IsCancelled { get; set; }   // soft-delete: reports must still tie out
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<PurchaseLine> Lines { get; set; } = new();
}

public class PurchaseLine
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>Batch created by this line. Every purchase line makes one batch.</summary>
    public int BatchId { get; set; }
    public Batch? Batch { get; set; }

    /// <summary>Packs received, e.g. 10 sacks. Null when bought loose.</summary>
    public decimal? PackQty { get; set; }

    /// <summary>Quantity in BaseUnit. For variable weight this is the weighed amount.</summary>
    public decimal Qty { get; set; }

    /// <summary>Rate per BaseUnit before other charges.</summary>
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}
