namespace ShopApp.Domain.Entities;

/// <summary>
/// A lot of an item received at one cost with one expiry date.
/// Costing is batch-wise actual cost, so margin per invoice is exact.
/// </summary>
public class Batch
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public string BatchNo { get; set; } = string.Empty;
    public DateTime? MfgDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// Landed cost per BaseUnit: supplier rate plus apportioned freight,
    /// loading and cold-storage. This is the number margin is calculated from.
    /// </summary>
    public decimal CostPrice { get; set; }

    public string? StorageLocation { get; set; }   // "Cold Room 2", "Godown A"
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public List<StockMove> Moves { get; set; } = new();
}
