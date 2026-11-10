using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo01.Returning;

public static class Seed
{
    /// <summary>Fixed "now" so the same bookings are expired on every run.</summary>
    public static readonly DateTime Cutoff = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static int BookingCount(DemoSettings s) => s.Scale(1_000, 10_000, 100_000);

    public static List<Booking> GenerateBookings(int count)
    {
        var random = new Random(20260101); // deterministic
        return Enumerable.Range(1, count).Select(id =>
        {
            // Bookings from 60 days in the past to 300 days in the future → roughly 1 in 6 is expired
            var start = Cutoff.AddDays(random.Next(-60, 300)).AddHours(random.Next(7, 19));
            return new Booking
            {
                Id = id,
                UserId = random.Next(1, 2_001),
                RoomId = random.Next(1, 201),
                StartsAt = start,
                EndsAt = start.AddHours(random.Next(1, 5))
            };
        }).ToList();
    }

    /// <summary>Puts the bookings table back to its seeded state (the demo deletes rows, so we reset each run).</summary>
    public static async Task ResetBookingsAsync(DemoSettings settings)
    {
        await using var db = new BookingDbContext(settings, useReturningExtensions: false);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [BookingAudits]; TRUNCATE TABLE [Bookings];");
        await BulkInsert.WriteAsync(settings.ConnectionStringFor(BookingDbContext.DatabaseName), "Bookings",
            GenerateBookings(BookingCount(settings)));
    }
}
