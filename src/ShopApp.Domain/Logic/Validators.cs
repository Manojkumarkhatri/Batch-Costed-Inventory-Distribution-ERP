using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Logic;

public record ValidationIssue(string Field, string Message, bool IsWarning = false);

public record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => !i.IsWarning);
    public bool HasWarnings => Issues.Any(i => i.IsWarning);
    public static ValidationResult Ok() => new(Array.Empty<ValidationIssue>());

    public string ErrorText =>
        string.Join("\n", Issues.Where(i => !i.IsWarning).Select(i => i.Message));
    public string WarningText =>
        string.Join("\n", Issues.Where(i => i.IsWarning).Select(i => i.Message));
}

/// <summary>
/// Pure validation, deliberately outside the ViewModel so it can be tested
/// without launching a window and reused if a second entry path ever appears
/// (import, barcode scan, bulk edit).
///
/// Errors block saving. Warnings do not - a shopkeeper sometimes really does
/// sell below cost to clear stock, and the app should not argue with him.
/// </summary>
public static class ItemValidator
{
    public static ValidationResult Validate(Item item, bool nameAlreadyExists)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(item.Name))
            issues.Add(new("Name", "Item name is required."));
        else if (item.Name.Trim().Length < 2)
            issues.Add(new("Name", "Item name is too short."));

        if (nameAlreadyExists)
            issues.Add(new("Name", $"An item named \"{item.Name.Trim()}\" already exists."));

        if (string.IsNullOrWhiteSpace(item.BaseUnit))
            issues.Add(new("BaseUnit", "Base unit is required (Kg, Litre, Piece...)."));

        // Dual units only make sense as a pair.
        var hasAlt = !string.IsNullOrWhiteSpace(item.AltUnit);
        if (hasAlt)
        {
            if (item.ConversionFactor <= 0)
                issues.Add(new("ConversionFactor",
                    "Conversion factor must be greater than zero when a pack unit is set."));
            else if (item.ConversionFactor == 1m)
                issues.Add(new("ConversionFactor",
                    $"1 {item.AltUnit} = 1 {item.BaseUnit}. Is that right?", IsWarning: true));

            if (string.Equals(item.AltUnit?.Trim(), item.BaseUnit?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                issues.Add(new("AltUnit", "Pack unit and base unit cannot be the same."));
        }
        else if (item.IsVariableWeight)
        {
            issues.Add(new("IsVariableWeight",
                "Variable weight needs a pack unit (e.g. Sack) to weigh against."));
        }

        if (item.DefaultPurchasePrice < 0)
            issues.Add(new("DefaultPurchasePrice", "Purchase price cannot be negative."));
        if (item.DefaultSalePrice < 0)
            issues.Add(new("DefaultSalePrice", "Sale price cannot be negative."));
        if (item.ReorderLevel < 0)
            issues.Add(new("ReorderLevel", "Reorder level cannot be negative."));
        if (item.NearExpiryDays < 0)
            issues.Add(new("NearExpiryDays", "Near-expiry days cannot be negative."));

        // Warning, not an error: clearance sales are legitimate.
        if (item.DefaultSalePrice > 0 && item.DefaultPurchasePrice > 0 &&
            item.DefaultSalePrice < item.DefaultPurchasePrice)
            issues.Add(new("DefaultSalePrice",
                "Sale price is below purchase price - this item will sell at a loss.",
                IsWarning: true));

        // Frozen and liquid goods without expiry tracking is usually an oversight.
        if (item.Category is ItemCategory.Frozen or ItemCategory.Liquid &&
            item.NearExpiryDays == 0)
            issues.Add(new("NearExpiryDays",
                $"{item.Category} items usually need expiry tracking. Set near-expiry days above zero.",
                IsWarning: true));

        return new ValidationResult(issues);
    }
}

public static class PartyValidator
{
    public static ValidationResult Validate(Party party, bool nameAlreadyExists)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(party.Name))
            issues.Add(new("Name", "Party name is required."));
        else if (party.Name.Trim().Length < 2)
            issues.Add(new("Name", "Party name is too short."));

        if (nameAlreadyExists)
            issues.Add(new("Name", $"A party named \"{party.Name.Trim()}\" already exists."));

        // Direction comes from the To Receive / To Pay radio, so the figure
        // itself must never be negative.
        if (party.OpeningBalance < 0)
            issues.Add(new("OpeningBalance",
                "Enter the opening balance as a positive amount and pick To Receive or To Pay."));

        if (party.CreditLimit < 0)
            issues.Add(new("CreditLimit", "Credit limit cannot be negative."));

        if (!string.IsNullOrWhiteSpace(party.Phone) && !LooksLikePhone(party.Phone))
            issues.Add(new("Phone", "Phone number looks unusual. Check it.", IsWarning: true));

        if (!string.IsNullOrWhiteSpace(party.Email) && !LooksLikeEmail(party.Email))
            issues.Add(new("Email", "Email address looks unusual. Check it.", IsWarning: true));

        return new ValidationResult(issues);
    }

    /// <summary>Deliberately loose - just enough to catch a typo.</summary>
    public static bool LooksLikeEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        if (email.IndexOf('@', at + 1) >= 0) return false;
        var domain = email[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.')
               && !email.Contains(' ');
    }

    /// <summary>Deliberately loose. Pakistani numbers get written many ways.</summary>
    public static bool LooksLikePhone(string phone)
    {
        var digits = phone.Count(char.IsDigit);
        if (digits < 7 || digits > 15) return false;
        return phone.All(c => char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')');
    }
}
