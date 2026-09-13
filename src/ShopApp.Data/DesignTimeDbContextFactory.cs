using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopApp.Data;

/// <summary>
/// Used ONLY by the `dotnet ef` command line tools.
///
/// At runtime the DbContext is built by the DI container in App.xaml.cs, but
/// EF's tooling never runs that code - it loads this assembly and looks for a
/// factory instead. Without this class, `dotnet ef migrations add` fails with
/// "Unable to resolve service for type DbContextOptions".
///
/// The connection string here only needs to be valid enough to build the
/// model. Migrations are generated from the C# model, not from a live database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopApp");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "shop.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        return new AppDbContext(options);
    }
}
