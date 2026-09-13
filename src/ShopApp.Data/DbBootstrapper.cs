using Microsoft.EntityFrameworkCore;

namespace ShopApp.Data;

public static class DbBootstrapper
{
    /// <summary>
    /// Run once at startup. Applies migrations and turns on the PRAGMAs that
    /// matter for a shop counter PC on unstable mains power.
    /// </summary>
    public static void Initialise(AppDbContext db)
    {
        db.Database.Migrate();

        // WAL: survives a power cut mid-write far better than the default journal.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        // FULL: every commit is flushed to disk. Slightly slower, much safer.
        // On this volume of transactions the cost is invisible.
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=FULL;");

        // EF respects the model's delete behaviour, but enforce at DB level too.
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
    }

    /// <summary>Returns true if the file is a healthy SQLite database.</summary>
    public static bool IntegrityCheck(AppDbContext db)
    {
        var result = db.Database
            .SqlQueryRaw<string>("PRAGMA integrity_check;")
            .AsEnumerable()
            .FirstOrDefault();
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }
}
