namespace ShopApp.Domain.Entities;

/// <summary>Single-row table. Company profile, invoice numbering, backup config.</summary>
public class AppSettings
{
    public int Id { get; set; } = 1;

    public string BusinessName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? LogoPath { get; set; }

    /// <summary>
    /// Empty by default: his invoice prints "Invoice No.: 7", a plain number.
    /// Set to "INV-" only if he later asks for a prefix.
    /// </summary>
    public string InvoicePrefix { get; set; } = "";
    public int InvoiceNextNumber { get; set; } = 1;

    /// <summary>True = numbering restarts each financial year. He does not want this.</summary>
    public bool InvoiceResetYearly { get; set; }

    /// <summary>1 = no zero padding, so 7 prints as "7" not "00007".</summary>
    public int InvoiceNumberPadding { get; set; } = 1;

    /// <summary>Where encrypted backups are written. USB drive, second disk, or synced folder.</summary>
    public string? BackupFolder { get; set; }
    public int BackupKeepCount { get; set; } = 30;
    public DateTime? LastBackupAt { get; set; }

    /// <summary>DPAPI-protected backup key. Never stored in plain text.</summary>
    public byte[]? ProtectedBackupKey { get; set; }
    public byte[]? BackupKeySalt { get; set; }

    /// <summary>Tax is off. Flip this if he registers with FBR later.</summary>
    public bool TaxEnabled { get; set; }

    /// <summary>
    /// What was in the till on the day he started using the app. Everything
    /// after that is worked out from what has moved, but the starting figure
    /// has to be told to it - there is no transaction that created it.
    /// </summary>
    public decimal OpeningCashInHand { get; set; }

    // Payment vouchers carry their own series, separate from invoices and
    // from each other. A receipt numbered the same as a payment made would
    // be impossible to discuss on the phone.
    public string PaymentInPrefix { get; set; } = "RV-";
    public int PaymentInNextNumber { get; set; } = 1;
    public string PaymentOutPrefix { get; set; } = "PV-";
    public int PaymentOutNextNumber { get; set; } = 1;
    public int PaymentNumberPadding { get; set; } = 3;
}
