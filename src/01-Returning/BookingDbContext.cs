using Common;
using EFCore.ReturningExtensions.SqlServer.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Demo01.Returning;

public class BookingDbContext(DemoSettings settings, bool useReturningExtensions, SqlCapture? capture = null) : DbContext
{
    public const string DatabaseName = "EfExtDemo01_Returning";

    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingAudit> BookingAudits => Set<BookingAudit>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseDemoSqlServer(settings, DatabaseName, capture, sql =>
        {
            // The only difference between BEFORE and AFTER contexts:
            if (useReturningExtensions)
                sql.UseReturningExtensions();
        });

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Ids are assigned by the seeder so every run deletes exactly the same bookings.
        model.Entity<Booking>().Property(b => b.Id).ValueGeneratedNever();
        model.Entity<Booking>().HasIndex(b => b.EndsAt); // the cleanup filters on EndsAt
        model.Entity<BookingAudit>().Property(a => a.Method).HasMaxLength(20);
        model.Entity<BookingAudit>().HasIndex(a => a.BookingId);
    }
}
