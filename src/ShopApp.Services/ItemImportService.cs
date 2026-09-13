using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record ImportSummary(
    int Added, int Updated, int Skipped,
    IReadOnlyList<string> Errors)
{
    public bool AnythingHappened => Added > 0 || Updated > 0;

    public string Describe()
    {
        var parts = new List<string>();
        if (Added > 0) parts.Add($"{Added} added");
        if (Updated > 0) parts.Add($"{Updated} updated");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        if (parts.Count == 0) parts.Add("nothing imported");
        return string.Join(", ", parts);
    }
}

public class ItemImportService
{
    private readonly AppDbContext _db;

    public ItemImportService(AppDbContext db) => _db = db;

    /// <summary>
    /// Bulk import. Existing items are matched by name (case-insensitive).
    ///
    /// updateExisting=false is the safe default: it will not overwrite prices
    /// or units he has already tuned by hand. Turning it on is useful when a
    /// corrected supplier list arrives.
    /// </summary>
    public ImportSummary Import(string csvText, bool updateExisting = false)
    {
        var parsed = ItemCsvParser.Parse(csvText);
        if (parsed.Rows.Count == 0)
            return new ImportSummary(0, 0, 0, parsed.Errors);

        var errors = parsed.Errors.ToList();
        int added = 0, updated = 0, skipped = 0;

        var existing = _db.Items
            .ToDictionary(i => i.Name.ToLowerInvariant(), i => i);

        using var tx = _db.Database.BeginTransaction();
        try
        {
            foreach (var row in parsed.Rows)
            {
                var key = row.Name.ToLowerInvariant();

                if (existing.TryGetValue(key, out var found))
                {
                    if (!updateExisting) { skipped++; continue; }

                    found.Category = row.Category;
                    found.BaseUnit = row.BaseUnit;
                    found.AltUnit = row.AltUnit;
                    found.ConversionFactor = row.ConversionFactor;
                    found.NearExpiryDays = row.NearExpiryDays;
                    if (row.PurchasePrice > 0) found.DefaultPurchasePrice = row.PurchasePrice;
                    if (row.SalePrice > 0) found.DefaultSalePrice = row.SalePrice;
                    if (row.ReorderLevel > 0) found.ReorderLevel = row.ReorderLevel;
                    updated++;
                    continue;
                }

                var item = new Item
                {
                    Name = row.Name,
                    Category = row.Category,
                    BaseUnit = row.BaseUnit,
                    AltUnit = row.AltUnit,
                    ConversionFactor = row.ConversionFactor,
                    DefaultPurchasePrice = row.PurchasePrice,
                    DefaultSalePrice = row.SalePrice,
                    ReorderLevel = row.ReorderLevel,
                    NearExpiryDays = row.NearExpiryDays,
                    IsActive = true
                };

                // Same rules as manual entry - the importer is not a back door.
                var validation = ItemValidator.Validate(item, nameAlreadyExists: false);
                if (validation.HasErrors)
                {
                    errors.Add($"Line {row.LineNumber} ({row.Name}): {validation.ErrorText}");
                    continue;
                }

                _db.Items.Add(item);
                existing[key] = item;
                added++;
            }

            _db.SaveChanges();
            tx.Commit();
            _db.ChangeTracker.Clear();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _db.ChangeTracker.Clear();
            return new ImportSummary(0, 0, 0, new[] { $"Import failed: {ex.Message}" });
        }

        return new ImportSummary(added, updated, skipped, errors);
    }
}
