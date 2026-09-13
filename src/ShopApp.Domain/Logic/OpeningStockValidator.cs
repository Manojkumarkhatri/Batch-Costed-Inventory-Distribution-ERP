using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Logic;

/// <summary>One row of what is physically in the godown on go-live day.</summary>
public record OpeningStockDraft(
    int RowNumber,
    int ItemId,
    string ItemName,
    string? BatchNo,
    decimal Qty,
    decimal CostPrice,
    DateTime? MfgDate,
    DateTime? ExpiryDate,
    ItemCategory Category,
    bool AlreadyHasOpening);

/// <summary>
/// Opening stock is entered once, on the day he starts using the app.
/// Whatever he types becomes the app's truth for ever, so the checks here are
/// deliberately strict: a wrong opening figure quietly corrupts every stock
/// and margin report until somebody notices months later.
/// </summary>
public static class OpeningStockValidator
{
    public static ValidationResult Validate(
        DateTime asOfDate, IReadOnlyList<OpeningStockDraft> rows)
    {
        var issues = new List<ValidationIssue>();

        if (rows.Count == 0)
            issues.Add(new("Rows", "Add at least one item."));

        if (asOfDate.Date > DateTime.Today)
            issues.Add(new("AsOfDate", "The opening date cannot be in the future."));

        foreach (var row in rows)
        {
            var label = string.IsNullOrWhiteSpace(row.ItemName)
                ? $"Row {row.RowNumber}"
                : $"Row {row.RowNumber} ({row.ItemName})";

            if (row.ItemId <= 0)
                issues.Add(new("Item", $"{label}: choose an item."));

            if (row.Qty <= 0)
                issues.Add(new("Qty", $"{label}: quantity must be more than zero."));

            if (row.CostPrice < 0)
                issues.Add(new("CostPrice", $"{label}: cost price cannot be negative."));
            else if (row.CostPrice == 0)
                // Zero cost makes every future sale of this batch look like pure
                // profit, which is the single most common opening-stock mistake.
                issues.Add(new("CostPrice",
                    $"{label}: cost price is zero, so this stock will show 100% profit when sold.",
                    IsWarning: true));

            if (row.AlreadyHasOpening)
                issues.Add(new("Item",
                    $"{label}: opening stock has already been entered for this item. " +
                    "Entering it twice would double the quantity.",
                    IsWarning: true));

            if (row.ExpiryDate is DateTime exp)
            {
                if (row.MfgDate is DateTime mfg && exp.Date <= mfg.Date)
                    issues.Add(new("ExpiryDate",
                        $"{label}: expiry must be after the manufacture date."));

                if (exp.Date < asOfDate.Date)
                    issues.Add(new("ExpiryDate",
                        $"{label}: this stock has already expired. Should it be counted?",
                        IsWarning: true));
            }
            else if (row.Category is ItemCategory.Frozen or ItemCategory.Liquid)
            {
                issues.Add(new("ExpiryDate",
                    $"{label}: {row.Category} stock without an expiry date will never " +
                    "appear on the near-expiry report.",
                    IsWarning: true));
            }

            if (row.MfgDate is DateTime m && m.Date > asOfDate.Date)
                issues.Add(new("MfgDate", $"{label}: manufacture date is after the opening date."));
        }

        // Same item and batch twice would merge two different costs into one lot.
        var dupes = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.BatchNo))
            .GroupBy(r => (r.ItemId, Batch: r.BatchNo!.Trim().ToLowerInvariant()))
            .Where(g => g.Count() > 1);

        foreach (var d in dupes)
            issues.Add(new("BatchNo",
                $"Batch \"{d.First().BatchNo}\" is listed twice for {d.First().ItemName}."));

        return new ValidationResult(issues);
    }
}
