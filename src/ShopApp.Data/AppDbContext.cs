using Microsoft.EntityFrameworkCore;
using ShopApp.Data.Converters;
using ShopApp.Domain.Entities;

namespace ShopApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMove> StockMoves => Set<StockMove>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<InvoicePrintOptions> InvoicePrintOptions => Set<InvoicePrintOptions>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var money = new MoneyConverter();
        var nMoney = new NullableMoneyConverter();
        var qty = new QtyConverter();
        var nQty = new NullableQtyConverter();

        b.Entity<Party>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.GroupName).HasMaxLength(100);
            e.Property(x => x.OpeningBalance).HasConversion(money);
            e.Property(x => x.CreditLimit).HasConversion(money);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.GroupName);
            // computed from stored columns, never persisted
            e.Ignore(x => x.SignedOpeningBalance);
        });

        b.Entity<Item>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.BaseUnit).IsRequired().HasMaxLength(20);
            e.Property(x => x.ConversionFactor).HasConversion(qty);
            e.Property(x => x.DefaultPurchasePrice).HasConversion(money);
            e.Property(x => x.DefaultSalePrice).HasConversion(money);
            e.Property(x => x.ReorderLevel).HasConversion(qty);
            e.Property(x => x.TaxRate).HasConversion(money);
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Barcode);
            e.HasIndex(x => x.Category);
        });

        b.Entity<Batch>(e =>
        {
            e.Property(x => x.BatchNo).IsRequired().HasMaxLength(50);
            e.Property(x => x.CostPrice).HasConversion(money);
            e.HasOne(x => x.Item).WithMany(i => i.Batches)
             .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemId, x.BatchNo });
            // drives the near-expiry report
            e.HasIndex(x => x.ExpiryDate);
        });

        b.Entity<StockMove>(e =>
        {
            e.Property(x => x.Qty).HasConversion(qty);
            e.HasOne(x => x.Batch).WithMany(bt => bt.Moves)
             .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.BatchId);
            e.HasIndex(x => x.MovedAt);
            e.HasIndex(x => new { x.RefTable, x.RefId });
        });

        b.Entity<Purchase>(e =>
        {
            e.Property(x => x.SubTotal).HasConversion(money);
            e.Property(x => x.Discount).HasConversion(money);
            e.Property(x => x.OtherCharges).HasConversion(money);
            e.Property(x => x.Total).HasConversion(money);
            e.HasOne(x => x.Supplier).WithMany()
             .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        b.Entity<PurchaseLine>(e =>
        {
            e.Property(x => x.PackQty).HasConversion(nQty);
            e.Property(x => x.Qty).HasConversion(qty);
            e.Property(x => x.Rate).HasConversion(money);
            e.Property(x => x.Amount).HasConversion(money);
            e.HasOne(x => x.Purchase).WithMany(p => p.Lines)
             .HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item).WithMany()
             .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany()
             .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Sale>(e =>
        {
            e.Property(x => x.InvoiceNo).IsRequired().HasMaxLength(40);
            e.Property(x => x.SubTotal).HasConversion(money);
            e.Property(x => x.Discount).HasConversion(money);
            e.Property(x => x.Total).HasConversion(money);
            e.HasOne(x => x.Customer).WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            // no duplicate invoice numbers, ever
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.HasIndex(x => x.Date);
        });

        b.Entity<SaleLine>(e =>
        {
            e.Property(x => x.PackQty).HasConversion(nQty);
            e.Property(x => x.Qty).HasConversion(qty);
            e.Property(x => x.Rate).HasConversion(money);
            e.Property(x => x.Amount).HasConversion(money);
            e.Property(x => x.CostAtSale).HasConversion(money);
            e.HasOne(x => x.Sale).WithMany(s => s.Lines)
             .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item).WithMany()
             .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany()
             .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Amount).HasConversion(money);
            e.HasOne(x => x.Party).WithMany()
             .HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        b.Entity<PaymentAllocation>(e =>
        {
            e.Property(x => x.Amount).HasConversion(money);
            e.HasOne(x => x.Payment).WithMany(p => p.Allocations)
             .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Amount).HasConversion(money);
            e.HasIndex(x => x.Date);
        });

        b.Entity<BankAccount>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.OpeningBalance).HasConversion(money);
            e.Property(x => x.AccountNumber).HasMaxLength(40);
            e.Property(x => x.BankName).HasMaxLength(120);
            e.Property(x => x.BranchOrIban).HasMaxLength(60);
            e.Property(x => x.AccountHolder).HasMaxLength(120);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<BankTransaction>(e =>
        {
            e.Property(x => x.Amount).HasConversion(money);
            e.Property(x => x.Description).HasMaxLength(300);

            e.HasOne(x => x.Account)
             .WithMany(a => a.Transactions)
             .HasForeignKey(x => x.BankAccountId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.BankAccountId);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.TransferGroup);
        });

        b.Entity<InvoicePrintOptions>(e =>
        {
            e.Property(x => x.PaymentType).HasMaxLength(30);
            // one row per sale, so reprinting reproduces the original exactly
            e.HasIndex(x => x.SaleId).IsUnique();
        });

        b.Entity<AppSettings>(e =>
        {
            // Money is stored as integer paisa like every other amount, so the
            // opening cash figure needs the same converter.
            e.Property(x => x.OpeningCashInHand).HasConversion(money);

            e.HasData(new AppSettings { Id = 1 });
        });
    }
}
