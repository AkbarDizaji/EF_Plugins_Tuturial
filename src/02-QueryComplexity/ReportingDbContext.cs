using Common;
using EfQueryComplexity;
using Microsoft.EntityFrameworkCore;

namespace Demo02.QueryComplexity;

public class ReportingDbContext(DemoSettings settings, bool protect, SqlCapture? capture = null) : DbContext
{
    public const string DatabaseName = "EfExtDemo02_QueryComplexity";

    /// <summary>
    /// The guardrails for the reporting API. A query is rejected when any measured value is greater than its level.
    /// </summary>
    public static readonly QueryComplexityLimits ReportingLimits = new(
        MaxNodes: 1_000,              // expression nodes in the whole query
        MaxDepth: 25,                 // nesting depth of the expression tree
        MaxOperators: 30,             // Where/Select/OrderBy/Include/... calls
        MaxNavigationDepth: 3,        // o.Customer.Address.Country = 3
        MaxIncludes: 2,               // Include/ThenInclude calls
        MaxIncludeDepth: 2,           // navigations in one Include chain
        MaxTake: 1_000,               // value passed to Take (checked on every execution)
        MaxInValues: 1_000,           // size of a Contains(...) list
        RejectUnbounded: true,        // a query returning rows must have Take (or be a lookup by key)
        MaxSingleQueryCollections: 1  // collections joined side by side in ONE SQL query (cartesian explosion)
    );

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseDemoSqlServer(settings, DatabaseName, capture);

        // The only difference between BEFORE and AFTER contexts.
        // Same levels for logAt and throwAt means "throw, don't just log".
        if (protect)
            options.UseQueryComplexity(logAt: ReportingLimits, throwAt: ReportingLimits);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Order>(o =>
        {
            o.Property(x => x.Id).ValueGeneratedNever();
            o.Property(x => x.Status).HasMaxLength(20);
            o.HasIndex(x => x.CustomerId);
            o.HasIndex(x => x.CreatedAt);
        });
        model.Entity<OrderItem>(i =>
        {
            i.ToTable("OrderItems");
            i.Property(x => x.Id).ValueGeneratedNever();
            i.Property(x => x.Sku).HasMaxLength(20);
            i.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
        model.Entity<Payment>(p =>
        {
            p.ToTable("Payments");
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Property(x => x.Amount).HasPrecision(18, 2);
        });
        model.Entity<OrderHistory>(h =>
        {
            h.ToTable("OrderHistory");
            h.Property(x => x.Id).ValueGeneratedNever();
            h.Property(x => x.Status).HasMaxLength(20);
        });
        // Foreign keys (OrderId) get an index by convention, which every Include uses.
    }
}
