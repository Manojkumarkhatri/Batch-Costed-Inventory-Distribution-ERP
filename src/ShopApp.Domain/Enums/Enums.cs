namespace ShopApp.Domain.Enums;

/// <summary>Physical category of a food item. Drives storage and handling rules.</summary>
public enum ItemCategory
{
    Solid = 1,
    Liquid = 2,
    Frozen = 3
}

// PartyType was removed. His approved party screen has no customer/supplier
// switch, and several of his parties are both. Any party may appear on a sale
// or a purchase; Group is used for his own categorisation.

/// <summary>
/// Every change in stock is one of these. StockMove is append-only:
/// on-hand quantity is always SUM(Qty), never a stored column.
/// </summary>
public enum StockMoveType
{
    Purchase = 1,       // + stock in from supplier
    Sale = 2,           // - stock out to customer
    PurchaseReturn = 3, // - returned to supplier
    SaleReturn = 4,     // + returned by customer
    Adjustment = 5,     // +/- manual correction, requires a reason
    Opening = 6         // + opening stock at go-live
}

/// <summary>Reason codes for Adjustment moves. Feeds the shrinkage report.</summary>
public enum AdjustmentReason
{
    Damage = 1,
    Spillage = 2,       // liquids
    ThawLoss = 3,       // frozen: cold-chain failure
    WeightLoss = 4,     // solids: moisture loss over time
    Expired = 5,
    Theft = 6,
    StockCount = 7,     // physical count correction
    Other = 99
}

public enum PaymentDirection
{
    In = 1,   // money received from a customer
    Out = 2   // money paid to a supplier
}

public enum PaymentMode
{
    Cash = 1,
    Bank = 2,
    Cheque = 3,
    Online = 4
}
