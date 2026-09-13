namespace ShopApp.Domain.Logic;

/// <summary>Sack to Kg, Drum to Litre, Carton to Piece.</summary>
public static class UnitConverter
{
    public static decimal PackToBase(decimal packQty, decimal conversionFactor)
    {
        if (conversionFactor <= 0)
            throw new ArgumentException("Conversion factor must be greater than zero.", nameof(conversionFactor));
        return Money.RoundQty(packQty * conversionFactor);
    }

    public static decimal BaseToPack(decimal baseQty, decimal conversionFactor)
    {
        if (conversionFactor <= 0)
            throw new ArgumentException("Conversion factor must be greater than zero.", nameof(conversionFactor));
        return Money.RoundQty(baseQty / conversionFactor);
    }
}
