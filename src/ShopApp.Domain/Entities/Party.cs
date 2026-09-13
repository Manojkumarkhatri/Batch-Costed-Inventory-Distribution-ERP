namespace ShopApp.Domain.Entities;

/// <summary>
/// A party he trades with. Fields mirror the screen he approved:
/// Name, Contact, Group, Opening Balance (+ To Receive / To Pay),
/// Credit Limit, Billing Address, Email.
///
/// There is deliberately NO Customer/Supplier type. Any party can appear on a
/// sale or a purchase, which matches how he actually works - several of his
/// parties are both. Use Group for his own categorisation.
/// </summary>
public class Party
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Free-text category, e.g. "Bakery", "Wholesale", "Supplier".</summary>
    public string? GroupName { get; set; }

    /// <summary>Billing address as printed on the invoice.</summary>
    public string? Address { get; set; }

    /// <summary>
    /// Always stored as a positive figure. The direction lives in
    /// OpeningIsReceivable, matching his To Receive / To Pay radio buttons.
    /// Signed maths happens in SignedOpeningBalance, never in the UI.
    /// </summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>True = they owe him (To Receive). False = he owes them (To Pay).</summary>
    public bool OpeningIsReceivable { get; set; } = true;

    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;

    /// <summary>0 means no limit is enforced.</summary>
    public decimal CreditLimit { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Positive = they owe him. Negative = he owes them.</summary>
    public decimal SignedOpeningBalance =>
        OpeningIsReceivable ? OpeningBalance : -OpeningBalance;
}
