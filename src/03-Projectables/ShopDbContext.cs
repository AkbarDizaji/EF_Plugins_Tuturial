using Common;
using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;

namespace Demo03.Projectables;

/// <summary>Shared mapping for both entity versions (both point at the same Orders / OrderItems tables).</summary>
public abstract class ShopDbContextBase(DemoSettings settings, SqlCapture? capture) : DbContext
{
    public const string DatabaseName = "EfExtDemo03_Projectables";

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseDemoSqlServer(settings, DatabaseName, capture);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<OrderItem>(i =>
        {
            i.ToTable("OrderItems");
            i.Property(x => x.Id).ValueGeneratedNever();
            i.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
    }

    protected static void MapOrder<T>(ModelBuilder model) where T : class =>
        model.Entity<T>(o =>
        {
            o.ToTable("Orders");
            o.Property<int>("Id").ValueGeneratedNever();
            o.Property<string>("Number").HasMaxLength(20);
            o.Property<decimal>("DiscountRate").HasPrecision(4, 2);
            o.Property<decimal>("TaxRate").HasPrecision(4, 2);
            o.HasMany<OrderItem>("Items").WithOne().HasForeignKey(i => i.OrderId);
        });
}

/// <summary>BEFORE context: plain EF Core, PlainOrder entity.</summary>
public class BeforeDbContext(DemoSettings settings, SqlCapture? capture = null) : ShopDbContextBase(settings, capture)
{
    public DbSet<PlainOrder> Orders => Set<PlainOrder>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        MapOrder<PlainOrder>(model);
    }
}

/// <summary>AFTER context: UseProjectables() expands [Projectable] members before EF Core translates the query.</summary>
public class AfterDbContext(DemoSettings settings, SqlCapture? capture = null) : ShopDbContextBase(settings, capture)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        base.OnConfiguring(options);
        options.UseProjectables(); // the only extra line
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        MapOrder<Order>(model);
    }
}
