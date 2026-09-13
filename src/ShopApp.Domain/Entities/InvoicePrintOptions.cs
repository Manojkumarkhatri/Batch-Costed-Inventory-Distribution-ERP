namespace ShopApp.Domain.Entities;

/// <summary>
/// Print options chosen per invoice, matching his Vyapar "Invoice Print" screen.
///
/// He does not want these fixed globally - for one bill he shows Received and
/// Balance, for the next he does not. So the chosen options are saved against
/// the sale, and reprinting an old invoice reproduces exactly what he handed
/// the customer the first time.
///
/// Defaults below match the OFF state in his first screenshot.
/// </summary>
public class InvoicePrintOptions
{
    public int Id { get; set; }
    public int SaleId { get; set; }

    /// <summary>Adds a "Received" row to the Amounts block.</summary>
    public bool ShowReceivedAmount { get; set; }

    /// <summary>Adds a "Balance" row - what is still owed on this bill.</summary>
    public bool ShowBalanceAmount { get; set; }

    /// <summary>Adds "Current Balance" - the party's whole outstanding, not just this bill.</summary>
    public bool ShowPartyCurrentBalance { get; set; }

    /// <summary>Adds a total quantity line under the item table.</summary>
    public bool ShowTotalItemQuantity { get; set; }

    /// <summary>False prints 951,250. True prints 951,250.00.</summary>
    public bool ShowDecimals { get; set; }

    /// <summary>Thousands separators, e.g. 951,250.</summary>
    public bool GroupDigits { get; set; } = true;

    /// <summary>Lakh/crore instead of thousand/million in the words line.</summary>
    public bool AmountInWordsIndianFormat { get; set; }

    /// <summary>Stretches the item table down the page, as in his sample.</summary>
    public bool ExpandTableToWholePage { get; set; } = true;

    /// <summary>Pads the table with blank rows so short bills still look full.</summary>
    public int MinimumRowsInItemTable { get; set; }

    /// <summary>Printed under "Payment Type:", e.g. Credit or Cash.</summary>
    public string PaymentType { get; set; } = "Credit";

    public static InvoicePrintOptions Defaults() => new();

    public InvoicePrintOptions Clone() => new()
    {
        ShowReceivedAmount = ShowReceivedAmount,
        ShowBalanceAmount = ShowBalanceAmount,
        ShowPartyCurrentBalance = ShowPartyCurrentBalance,
        ShowTotalItemQuantity = ShowTotalItemQuantity,
        ShowDecimals = ShowDecimals,
        GroupDigits = GroupDigits,
        AmountInWordsIndianFormat = AmountInWordsIndianFormat,
        ExpandTableToWholePage = ExpandTableToWholePage,
        MinimumRowsInItemTable = MinimumRowsInItemTable,
        PaymentType = PaymentType
    };
}
