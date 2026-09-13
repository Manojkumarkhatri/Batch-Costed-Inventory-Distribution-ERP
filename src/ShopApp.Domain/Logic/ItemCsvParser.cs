using System.Globalization;
using ShopApp.Domain.Enums;

namespace ShopApp.Domain.Logic;

public record ItemImportRow(
    int LineNumber,
    string Name,
    ItemCategory Category,
    string BaseUnit,
    string? AltUnit,
    decimal ConversionFactor,
    decimal PurchasePrice,
    decimal SalePrice,
    decimal ReorderLevel,
    int NearExpiryDays);

public record ItemImportParseResult(
    IReadOnlyList<ItemImportRow> Rows,
    IReadOnlyList<string> Errors);

/// <summary>
/// Bulk item import. Typing 30-plus products by hand is slow and error-prone,
/// and the supplier lists already exist on paper or in Excel.
///
/// Expected header (case-insensitive, extra columns ignored):
///   Name, Category, BaseUnit, AltUnit, ConversionFactor,
///   PurchasePrice, SalePrice, ReorderLevel, NearExpiryDays
///
/// Only Name and BaseUnit are required. Everything else has a sensible default.
/// </summary>
public static class ItemCsvParser
{
    public static ItemImportParseResult Parse(string csvText)
    {
        var rows = new List<ItemImportRow>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(csvText))
            return new ItemImportParseResult(rows, new[] { "The file is empty." });

        // Strip UTF-8 BOM - Excel adds one and it corrupts the first header name.
        csvText = csvText.TrimStart('\uFEFF');

        var lines = csvText
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        var headerIndex = lines.FindIndex(l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
            return new ItemImportParseResult(rows, new[] { "The file is empty." });

        var header = SplitCsvLine(lines[headerIndex])
            .Select(h => h.Trim().Replace(" ", "").ToLowerInvariant())
            .ToList();

        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                var i = header.IndexOf(n.ToLowerInvariant());
                if (i >= 0) return i;
            }
            return -1;
        }

        var iName = Col("name", "item", "items", "itemname");
        var iCat = Col("category", "type");
        var iBase = Col("baseunit", "primaryunit", "unit");
        var iAlt = Col("altunit", "secondaryunit", "secondryunit", "packunit");
        var iConv = Col("conversionfactor", "conversion", "factor", "qtyperpack");
        var iPurch = Col("purchaseprice", "costprice", "purchase", "cost");
        var iSale = Col("saleprice", "sellingprice", "sale", "price");
        var iReorder = Col("reorderlevel", "reorder", "minstock");
        var iExpiry = Col("nearexpirydays", "expirydays", "expiry");

        if (iName < 0)
            errors.Add("No 'Name' column found. The first row must be a header.");
        if (iBase < 0)
            errors.Add("No 'BaseUnit' (or 'Primary Unit') column found.");
        if (errors.Count > 0)
            return new ItemImportParseResult(rows, errors);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var lineNo = i + 1;
            var f = SplitCsvLine(raw);

            string Field(int idx) =>
                idx >= 0 && idx < f.Count ? f[idx].Trim() : string.Empty;

            var name = Field(iName);
            if (string.IsNullOrWhiteSpace(name)) continue;   // blank filler row

            if (!seen.Add(name))
            {
                errors.Add($"Line {lineNo}: \"{name}\" appears more than once in the file.");
                continue;
            }

            var baseUnit = Field(iBase);
            if (string.IsNullOrWhiteSpace(baseUnit))
            {
                errors.Add($"Line {lineNo} ({name}): base unit is missing.");
                continue;
            }

            var category = ParseCategory(Field(iCat));

            var altUnit = Field(iAlt);
            if (string.IsNullOrWhiteSpace(altUnit)) altUnit = null;

            var convText = Field(iConv);
            decimal conversion = 1m;
            if (altUnit is not null)
            {
                if (!TryParseDecimal(convText, out conversion) || conversion <= 0)
                {
                    errors.Add($"Line {lineNo} ({name}): " +
                        $"pack unit \"{altUnit}\" needs a conversion factor greater than zero" +
                        (string.IsNullOrWhiteSpace(convText) ? "." : $", got \"{convText}\"."));
                    continue;
                }
            }

            if (altUnit is not null &&
                string.Equals(altUnit, baseUnit, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Line {lineNo} ({name}): pack unit and base unit are both \"{baseUnit}\".");
                continue;
            }

            TryParseDecimal(Field(iPurch), out var purchase);
            TryParseDecimal(Field(iSale), out var sale);
            TryParseDecimal(Field(iReorder), out var reorder);

            var expiryDays = int.TryParse(Field(iExpiry), out var d) && d >= 0
                ? d
                : DefaultExpiryDays(category);

            rows.Add(new ItemImportRow(
                lineNo, name, category, baseUnit, altUnit, conversion,
                purchase, sale, reorder, expiryDays));
        }

        if (rows.Count == 0 && errors.Count == 0)
            errors.Add("No item rows found below the header.");

        return new ItemImportParseResult(rows, errors);
    }

    private static int DefaultExpiryDays(ItemCategory c) => c switch
    {
        ItemCategory.Frozen => 30,
        ItemCategory.Liquid => 60,
        _ => 90
    };

    public static ItemCategory ParseCategory(string? text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "liquid" or "liquids" => ItemCategory.Liquid,
            "frozen" or "freeze" or "cold" => ItemCategory.Frozen,
            _ => ItemCategory.Solid       // "dry", "solid", blank, anything else
        };
    }

    /// <summary>Invariant culture: a file written on a machine with , decimals still parses.</summary>
    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cleaned = text.Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any,
            CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Minimal CSV split that respects double-quoted fields containing commas.</summary>
    public static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}
