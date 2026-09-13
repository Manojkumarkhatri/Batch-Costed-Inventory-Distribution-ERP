using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Logic;

/// <summary>One line as entered on the purchase screen, before it becomes a batch.</summary>
public record PurchaseLineDraft(
    int LineNumber,
    int ItemId,
    string ItemName,
    string? BatchNo,
    DateTime? MfgDate,
    DateTime? ExpiryDate,
    decimal Qty,
    decimal Rate,
    ItemCategory Category,
    bool RequiresExpiry);

public static class PurchaseValidator
{
    public static ValidationResult Validate(
        int supplierId,
        DateTime date,
        decimal discount,
        decimal otherCharges,
        IReadOnlyList<PurchaseLineDraft> lines)
    {
        var issues = new List<ValidationIssue>();

        if (supplierId <= 0)
            issues.Add(new("Supplier", "Choose a supplier."));

        if (lines.Count == 0)
            issues.Add(new("Lines", "Add at least one item."));

        if (date.Date > DateTime.Today)
            issues.Add(new("Date", "Purchase date is in the future.", IsWarning: true));

        if (otherCharges < 0)
            issues.Add(new("OtherCharges", "Other charges cannot be negative."));

        if (discount < 0)
            issues.Add(new("Discount", "Discount cannot be negative."));

        var subTotal = lines.Sum(l => Money.Round(l.Qty * l.Rate));
        if (discount > subTotal)
            issues.Add(new("Discount",
                $"Discount ({discount:N2}) is more than the total ({subTotal:N2})."));

        foreach (var line in lines)
        {
            var label = string.IsNullOrWhiteSpace(line.ItemName)
                ? $"Line {line.LineNumber}"
                : $"Line {line.LineNumber} ({line.ItemName})";

            if (line.ItemId <= 0)
                issues.Add(new("Item", $"{label}: choose an item."));

            if (line.Qty <= 0)
                issues.Add(new("Qty", $"{label}: quantity must be more than zero."));

            if (line.Rate < 0)
                issues.Add(new("Rate", $"{label}: rate cannot be negative."));
            else if (line.Rate == 0)
                issues.Add(new("Rate", $"{label}: rate is zero. Free goods?", IsWarning: true));

            if (line.ExpiryDate is DateTime exp)
            {
                if (line.MfgDate is DateTime mfg && exp.Date <= mfg.Date)
                    issues.Add(new("ExpiryDate",
                        $"{label}: expiry date must be after the manufacture date."));

                if (exp.Date < DateTime.Today)
                    issues.Add(new("ExpiryDate",
                        $"{label}: this stock has already expired.", IsWarning: true));
                else if (exp.Date < DateTime.Today.AddDays(7))
                    issues.Add(new("ExpiryDate",
                        $"{label}: expires within a week.", IsWarning: true));
            }
            else if (line.RequiresExpiry)
            {
                // Frozen and liquid goods without an expiry date defeat FEFO entirely.
                issues.Add(new("ExpiryDate",
                    $"{label}: {line.Category} stock should have an expiry date.",
                    IsWarning: true));
            }

            if (line.MfgDate is DateTime m && m.Date > DateTime.Today)
                issues.Add(new("MfgDate", $"{label}: manufacture date is in the future."));
        }

        // Two lines sharing one batch number for the same item would merge stock
        // that has different costs, which quietly corrupts margin reporting.
        var dupes = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.BatchNo))
            .GroupBy(l => (l.ItemId, BatchNo: l.BatchNo!.Trim().ToLowerInvariant()))
            .Where(g => g.Count() > 1);

        foreach (var d in dupes)
            issues.Add(new("BatchNo",
                $"Batch \"{d.First().BatchNo}\" is used twice for {d.First().ItemName}. " +
                "Give each batch its own number."));

        return new ValidationResult(issues);
    }
}

public static class BatchNumbering
{
    /// <summary>
    /// Auto batch number when the supplier's carton has none printed.
    /// Format: YYMMDD-NN, e.g. 260826-01. Short enough to write on a crate
    /// with a marker, which is how it will actually be used in the godown.
    /// </summary>
    public static string Suggest(DateTime date, int sequence) =>
        $"{date:yyMMdd}-{sequence:D2}";

    /// <summary>Next free sequence for a date, given batch numbers already in use.</summary>
    public static string NextFor(DateTime date, IEnumerable<string> existingBatchNos)
    {
        var prefix = $"{date:yyMMdd}-";
        var used = existingBatchNos
            .Where(b => b is not null && b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(b => int.TryParse(b[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return Suggest(date, used + 1);
    }
}
