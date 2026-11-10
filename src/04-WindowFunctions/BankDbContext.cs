using Common;
using Microsoft.EntityFrameworkCore;
using Zomp.EFCore.WindowFunctions.SqlServer;

namespace Demo04.WindowFunctions;

public class BankDbContext(DemoSettings settings, bool useWindowFunctions, SqlCapture? capture = null) : DbContext
{
    public const string DatabaseName = "EfExtDemo04_WindowFunctions";

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseDemoSqlServer(settings, DatabaseName, capture, sql =>
        {
            // The only difference between BEFORE and AFTER contexts:
            if (useWindowFunctions)
                sql.UseWindowFunctions();
        });

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>().Property(a => a.Id).ValueGeneratedNever();
        model.Entity<Account>().Property(a => a.Name).HasMaxLength(50);
        model.Entity<Transaction>(t =>
        {
            t.Property(x => x.Id).ValueGeneratedNever();
            t.Property(x => x.Kind).HasMaxLength(20);
            t.Property(x => x.Amount).HasPrecision(18, 2);
            t.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId);
            // Matches PARTITION BY AccountId ORDER BY CreatedAt, Id — and covers the selected columns,
            // so SQL Server reads the statement in order without a sort.
            t.HasIndex(x => new { x.AccountId, x.CreatedAt, x.Id })
             .IncludeProperties(x => new { x.Kind, x.Amount });
        });
    }
}
