using EFCore.ReturningExtensions.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Demo01.Returning;

/// <summary>
/// AFTER: one DELETE statement that also returns the rows it removed
/// (SQL Server: DELETE ... OUTPUT DELETED.*). What we audit is exactly what was deleted.
/// </summary>
public static class After
{
    public static async Task<CleanupResult> CleanupExpiredAsync(BookingDbContext db, DateTime cutoff)
    {
        // The library returns the deleted rows. It does NOT make the audit insert atomic with the delete —
        // that is still our job, so the transaction stays.
        await using var tx = await db.Database.BeginTransactionAsync();

        // Round trip 1: delete + return the deleted rows, in one statement.
        // (Project to an anonymous type or the entity; v10.2.3 can't project into a record constructor.)
        var deletedRows = await db.Bookings
            .AsNoTracking()
            .Where(b => b.EndsAt < cutoff)
            .Deleted(b => new { b.Id, b.UserId, b.EndsAt })
            .ToListAsync();

        var deleted = deletedRows.Select(r => new DeletedBooking(r.Id, r.UserId, r.EndsAt)).ToList();

        // Round trip 2+: audit exactly those rows
        db.BookingAudits.AddRange(deleted.Select(d => new BookingAudit
        {
            BookingId = d.BookingId,
            UserId = d.UserId,
            BookingEndedAt = d.EndsAt,
            DeletedAt = cutoff,
            Method = "After"
        }));
        await db.SaveChangesAsync();

        await tx.CommitAsync();
        return new CleanupResult(deleted, deleted.Count);
    }
}
