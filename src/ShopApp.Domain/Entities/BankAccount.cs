using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Entities;

/// <summary>
/// A bank account he moves money through.
///
/// Deliberately separate from Party: a bank is not somebody he trades with,
/// it is where the money sits. Mixing the two would put his own bank balance
/// into the receivables list.
/// </summary>
public class BankAccount
{
    public int Id { get; set; }

    /// <summary>What he calls it - "Bank al Habib", "HBL current".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The balance on the day he started using the app.</summary>
    public decimal OpeningBalance { get; set; }
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;

    // Printed on invoices when he wants customers to transfer directly.
    public string? AccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BranchOrIban { get; set; }
    public string? AccountHolder { get; set; }

    /// <summary>Whether these details appear at the foot of an invoice.</summary>
    public bool PrintOnInvoice { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<BankTransaction> Transactions { get; set; } = new();
}

/// <summary>
/// One movement on a bank account. Append-only, like StockMove: the balance is
/// the sum of these plus the opening figure, never a stored number that can
/// drift away from the rows that explain it.
///
/// Amount is SIGNED - positive puts money in, negative takes it out. One field
/// rather than a type-plus-magnitude pair that can disagree with itself.
/// </summary>
public class BankTransaction
{
    public int Id { get; set; }

    public int BankAccountId { get; set; }
    public BankAccount? Account { get; set; }

    public BankTxnType Type { get; set; }

    /// <summary>Positive in, negative out.</summary>
    public decimal Amount { get; set; }

    public DateTime Date { get; set; } = DateTime.Today;
    public string? Description { get; set; }

    /// <summary>
    /// Set on both halves of a transfer so the pair can be found together,
    /// and so deleting one takes the other with it.
    /// </summary>
    public Guid? TransferGroup { get; set; }

    /// <summary>The other account in a bank-to-bank transfer, for display.</summary>
    public int? CounterAccountId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsIn => Amount >= 0;
}
