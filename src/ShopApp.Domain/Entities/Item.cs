using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Entities;

/// <summary>
/// A product he trades. Stock is never stored here - it lives in StockMove.
/// Dual units: he may buy in Sack/Drum/Carton but sell in Kg/Litre/Piece.
/// </summary>
public class Item : ObservableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }        // internal SKU
    public string? Barcode { get; set; }
    public ItemCategory Category { get; set; } = ItemCategory.Solid;

    private string _baseUnit = "Kg";
    private string? _altUnit;
    private decimal _conversionFactor = 1m;

    /// <summary>The unit stock is held in. All StockMove quantities use this.</summary>
    public string BaseUnit
    {
        get => _baseUnit;
        set => SetField(ref _baseUnit, value);
    }

    /// <summary>Optional purchase/pack unit, e.g. "Sack".</summary>
    public string? AltUnit
    {
        get => _altUnit;
        set => SetField(ref _altUnit, value);
    }

    /// <summary>
    /// How many BaseUnit in one AltUnit. e.g. 1 Carton = 10 KG -> 10.
    /// Full property so the edit form can show a live "1 Carton = 10 KG"
    /// confirmation and a wrong value is caught before saving.
    /// </summary>
    public decimal ConversionFactor
    {
        get => _conversionFactor;
        set => SetField(ref _conversionFactor, value);
    }

    /// <summary>
    /// True when the pack has a nominal weight but is billed on actual weight.
    /// Forces the ActualWeight field on sale/purchase lines.
    /// </summary>
    public bool IsVariableWeight { get; set; }

    public decimal DefaultPurchasePrice { get; set; }
    public decimal DefaultSalePrice { get; set; }

    /// <summary>Triggers the low-stock report. In BaseUnit.</summary>
    public decimal ReorderLevel { get; set; }

    /// <summary>Days before expiry that this item shows on the near-expiry report.</summary>
    public int NearExpiryDays { get; set; } = 30;

    /// <summary>
    /// Kept at 0 and hidden in the UI - he trades tax-free today.
    /// Present so FBR registration later is a settings toggle, not a migration.
    /// </summary>
    public decimal TaxRate { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Batch> Batches { get; set; } = new();
}
