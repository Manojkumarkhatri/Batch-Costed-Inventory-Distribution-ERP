using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Entities;

public class Payment
{
    public int Id { get; set; }
    public int PartyId { get; set; }
    public Party? Party { get; set; }

    public PaymentDirection Direction { get; set; }
    public PaymentMode Mode { get; set; } = PaymentMode.Cash;

    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;

    public string? ReferenceNo { get; set; }

    /// <summary>
    /// Our own number for the voucher, e.g. "PV-004". Separate from
    /// ReferenceNo, which is the bank's or the cheque's - his number and
    /// theirs are different things and both get printed.
    /// </summary>
    public string? VoucherNo { get; set; }   // cheque number, transfer id
    public DateTime? ChequeDueDate { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<PaymentAllocation> Allocations { get; set; } = new();
}

/// <summary>Links a payment to the invoice(s) it settles. Drives receivables aging.</summary>
public class PaymentAllocation
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public int? SaleId { get; set; }
    public int? PurchaseId { get; set; }
    public decimal Amount { get; set; }
}

public class Expense
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;  // Freight, Rent, Electricity...
    public decimal Amount { get; set; }

    /// <summary>When the money actually left. Cash flow and the day book use this.</summary>
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>
    /// The stretch of time this money buys. Rent paid on 1 September covers
    /// the whole of September, so Profit and Loss charges a day of it per day
    /// rather than the lot on the 1st.
    ///
    /// Null on both means the expense belongs entirely to <see cref="Date"/> -
    /// a tank of fuel is spent the day it is bought.
    /// </summary>
    public DateTime? CoversFrom { get; set; }
    public DateTime? CoversTo { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>The first and last day this expense is charged over.</summary>
    public (DateTime From, DateTime To) Coverage =>
        CoversFrom is DateTime f && CoversTo is DateTime t && t >= f
            ? (f.Date, t.Date)
            : (Date.Date, Date.Date);

    public bool IsSpread => CoversFrom is not null && CoversTo is not null
                            && CoversTo.Value.Date > CoversFrom.Value.Date;
}
