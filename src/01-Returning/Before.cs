using Microsoft.EntityFrameworkCore;

namespace Demo01.Returning;

/// <summary>
/// BEFORE: read the expired bookings, then delete them, then write the audit.
/// Two separate statements look at the table at two different moments.
/// </summary>
public static class Before
{
    public static async Task<CleanupResult> CleanupExpiredAsync(
        BookingDbContext db,
        DateTime cutoff,
        Func<Task>? betweenSelectAndDelete = null) // lets the demo inject "another user" at the worst moment
    {
        // READ COMMITTED (the SQL Server default). Shared locks are released as soon as each row is read,
        // so nothing stops another connection from changing those rows before our DELETE runs.
        await using var tx = await db.Database.BeginTransactionAsync();

        // Round trip 1: what we *think* we are about to delete
        var toDelete = await db.Bookings
            .Where(b => b.EndsAt < cutoff)
            .Select(b => new DeletedBooking(b.Id, b.UserId, b.EndsAt))
            .ToListAsync();

        if (betweenSelectAndDelete is not null)
            await betweenSelectAndDelete();

        // Round trip 2: what we *actually* delete — the WHERE is evaluated again, against newer data
        var deletedCount = await db.Bookings
            .Where(b => b.EndsAt < cutoff)
            .ExecuteDeleteAsync();

        // Round trip 3+: audit is written from the SELECT result, which may no longer be true
        db.BookingAudits.AddRange(toDelete.Select(d => new BookingAudit
        {
            BookingId = d.BookingId,
            UserId = d.UserId,
            BookingEndedAt = d.EndsAt,
            DeletedAt = cutoff,
            Method = "Before"
        }));
        await db.SaveChangesAsync();

        await tx.CommitAsync();
        return new CleanupResult(toDelete, deletedCount);
    }
}
