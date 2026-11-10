using System.Runtime.CompilerServices;
using Common;
using Demo01.Returning;
using Microsoft.EntityFrameworkCore;

namespace EfCoreExtensionsDemo.Tests;

public class ReturningTests
{
    private static readonly DemoSettings Settings = TestSettings.Fast;
    private static readonly DateTime Cutoff = Seed.Cutoff;
    private static readonly List<Booking> Seeded = Seed.GenerateBookings(Seed.BookingCount(Settings));
    private static readonly int ExtendedId = Seeded.First(b => b.EndsAt < Cutoff).Id;
    private static readonly int EndedEarlyId = Seeded.First(b => b.EndsAt >= Cutoff).Id;

    [Fact]
    public async Task Before_ChangeBetweenSelectAndDelete_AuditDisagreesWithWhatWasDeleted()
    {
        await Seed.ResetBookingsAsync(Settings);
        await using var db = new BookingDbContext(Settings, useReturningExtensions: false);

        var result = await Before.CleanupExpiredAsync(db, Cutoff,
            () => ReturningDemo.OtherUsersChangeBookingsAsync(Settings, ExtendedId, EndedEarlyId, Cutoff));
        var check = await ReturningDemo.VerifyAsync(Settings, Seeded, result);

        // The race is real, even inside a READ COMMITTED transaction
        Assert.Equal([ExtendedId], check.AuditedNotDeleted);
        Assert.Equal([EndedEarlyId], check.DeletedNotAudited);
        // ...and invisible to a count check
        Assert.Equal(result.Audited.Count, result.DeletedCount);
    }

    [Fact]
    public async Task After_SameConcurrentChanges_AuditMatchesDeletionExactly()
    {
        await Seed.ResetBookingsAsync(Settings);
        await ReturningDemo.OtherUsersChangeBookingsAsync(Settings, ExtendedId, EndedEarlyId, Cutoff);
        await using var db = new BookingDbContext(Settings, useReturningExtensions: true);

        var result = await After.CleanupExpiredAsync(db, Cutoff);
        var check = await ReturningDemo.VerifyAsync(Settings, Seeded, result);

        Assert.True(check.Consistent);
        Assert.DoesNotContain(result.Audited, d => d.BookingId == ExtendedId);
        Assert.Contains(result.Audited, d => d.BookingId == EndedEarlyId);
    }

    [Fact]
    public async Task After_SendsOneDeleteWithOutputAndNoSelect()
    {
        await Seed.ResetBookingsAsync(Settings);
        var capture = new SqlCapture();
        await using var db = new BookingDbContext(Settings, useReturningExtensions: true, capture);
        db.Database.AutoSavepointsEnabled = false;

        await After.CleanupExpiredAsync(db, Cutoff);

        var delete = Assert.Single(capture.Commands, c => c.Contains("DELETE"));
        Assert.Contains("OUTPUT [DELETED].[Id], [DELETED].[UserId], [DELETED].[EndsAt]", delete);
        Assert.DoesNotContain(capture.Commands, c => c.TrimStart().StartsWith("SELECT") && c.Contains("[Bookings]"));
    }

    /// <summary>
    /// Real concurrency, no hooks: other connections keep moving bookings across the cutoff while the
    /// cleanup runs. Whatever interleaving happens, the audit must equal the set of rows that disappeared,
    /// and every audited row must have been expired at the moment it was deleted.
    /// </summary>
    [Fact]
    public async Task After_UnderConcurrentUpdates_AuditAlwaysEqualsDeletedRows()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await Seed.ResetBookingsAsync(Settings);
            using var stop = new CancellationTokenSource();
            var committed = new StrongBox<int>();
            var writers = Enumerable.Range(0, 3).Select(w => Task.Run(() => FlipBookingsAsync(w + iteration * 10, committed, stop.Token))).ToArray();
            while (Volatile.Read(ref committed.Value) == 0) await Task.Delay(5); // writers are live before cleanup starts

            // Under contention SQL Server may pick the cleanup as a deadlock victim. Our transaction must then
            // leave NO trace (nothing deleted, nothing audited) — the job simply retries, as it would in production.
            CleanupResult? result = null;
            for (var attempt = 0; result is null; attempt++)
            {
                try
                {
                    await using var db = new BookingDbContext(Settings, useReturningExtensions: true);
                    result = await After.CleanupExpiredAsync(db, Cutoff);
                }
                catch (Exception ex) when (IsDeadlock(ex) && attempt < 5)
                {
                    var rolledBack = await ReturningDemo.VerifyAsync(Settings, Seeded, new CleanupResult([], 0));
                    Assert.Equal(0, rolledBack.ActuallyDeleted);
                    Assert.Equal(0, rolledBack.AuditRows);
                }
            }

            await stop.CancelAsync();
            var updates = (await Task.WhenAll(writers)).Sum();

            var check = await ReturningDemo.VerifyAsync(Settings, Seeded, result);
            Assert.True(updates > 0, "the concurrent writers should have committed updates");
            Assert.True(check.Consistent, $"iteration {iteration}: {check}");
            Assert.All(result.Audited, d => Assert.True(d.EndsAt < Cutoff));
        }
    }

    private static async Task<int> FlipBookingsAsync(int seed, StrongBox<int> committed, CancellationToken stop)
    {
        var random = new Random(seed);
        var updates = 0;
        await using var db = new BookingDbContext(Settings, useReturningExtensions: false);
        while (!stop.IsCancellationRequested)
        {
            var id = random.Next(1, Seeded.Count + 1);
            var endsAt = Cutoff.AddHours(random.Next(-48, 48)); // either side of the cutoff
            try
            {
                var changed = await db.Bookings.Where(b => b.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.EndsAt, endsAt), CancellationToken.None);
                updates += changed;
                Interlocked.Add(ref committed.Value, changed);
            }
            catch (Exception ex) when (IsDeadlock(ex))
            {
                // This writer lost a deadlock against the DELETE; its update was rolled back. Keep going.
            }
        }
        return updates;
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is Microsoft.Data.SqlClient.SqlException { Number: 1205 })
                return true;
        return false;
    }
}
