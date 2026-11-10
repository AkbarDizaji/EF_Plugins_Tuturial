using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo01.Returning;

public sealed class ReturningDemo : IDemo
{
    public string Number => "01";
    public string Title => "ReturningExtensions";

    public async Task SeedAsync(DemoSettings settings, bool force = false)
    {
        await using var db = new BookingDbContext(settings, useReturningExtensions: false);
        if (force) await db.Database.EnsureDeletedAsync();
        await Seed.ResetBookingsAsync(settings);
    }

    public async Task ResetAsync(DemoSettings settings)
    {
        await using var db = new BookingDbContext(settings, useReturningExtensions: false);
        await db.Database.EnsureDeletedAsync();
    }

    public async Task RunAsync(DemoSettings settings)
    {
        var cutoff = Seed.Cutoff;
        var seeded = Seed.GenerateBookings(Seed.BookingCount(settings));
        // Two bookings that "other users" change while the cleanup job runs:
        var extended = seeded.First(b => b.EndsAt < cutoff);   // expired, but its owner extends it
        var endedEarly = seeded.First(b => b.EndsAt >= cutoff); // active, but an admin ends it early

        Ui.Header(Number, Title);
        Ui.Problem(
            $"A nightly job deletes expired bookings ({seeded.Count(b => b.EndsAt < cutoff):N0} of {seeded.Count:N0}) " +
            "and must keep an audit record of exactly what it deleted. " +
            $"While it runs, booking #{extended.Id} is extended by its owner and booking #{endedEarly.Id} is ended early by an admin.");

        // ---------------- BEFORE ----------------
        Ui.Before("SELECT the expired bookings → ExecuteDeleteAsync → insert audits from the SELECT result\n" +
                  "(inside a READ COMMITTED transaction)");
        await Seed.ResetBookingsAsync(settings);
        var beforeCapture = new SqlCapture();
        CleanupResult before;
        await using (var db = new BookingDbContext(settings, useReturningExtensions: false, beforeCapture))
        {
            db.Database.AutoSavepointsEnabled = false;
            before = await Before.CleanupExpiredAsync(db, cutoff,
                betweenSelectAndDelete: () => OtherUsersChangeBookingsAsync(settings, extended.Id, endedEarly.Id, cutoff));
        }
        var beforeCheck = await VerifyAsync(settings, seeded, before);

        Ui.Step(false, $"SELECT found {before.Audited.Count:N0} expired bookings → {before.Audited.Count:N0} audit records written");
        Ui.Step(false, $"DELETE removed {before.DeletedCount:N0} rows");
        Ui.Step(false, "Other users committed their changes between the SELECT and the DELETE");
        ShowMismatches(beforeCheck, after: false);

        // ---------------- AFTER ----------------
        Ui.After("Deleted(...) — one DELETE ... OUTPUT statement returns the rows it removed → audit exactly those");
        await Seed.ResetBookingsAsync(settings);
        // Same concurrent changes. With a single statement there is no gap to land in:
        // each change is committed either before the DELETE sees the row, or after it is gone.
        await OtherUsersChangeBookingsAsync(settings, extended.Id, endedEarly.Id, cutoff);
        var afterCapture = new SqlCapture();
        CleanupResult after;
        await using (var db = new BookingDbContext(settings, useReturningExtensions: true, afterCapture))
        {
            db.Database.AutoSavepointsEnabled = false;
            after = await After.CleanupExpiredAsync(db, cutoff);
        }
        var afterCheck = await VerifyAsync(settings, seeded, after);

        Ui.Step(true, $"DELETE ... OUTPUT removed and returned {after.DeletedCount:N0} rows → {after.Audited.Count:N0} audit records written");
        ShowMismatches(afterCheck, after: true);

        Ui.Table("First deleted rows, as returned by OUTPUT", ["Booking", "User", "Ended at"],
            after.Audited.OrderBy(d => d.BookingId).Take(5)
                .Select(d => new[] { $"#{d.BookingId}", $"{d.UserId}", $"{d.EndsAt:yyyy-MM-dd HH:mm}" }));

        // ---------------- RESULT ----------------
        Ui.Result(beforeCheck.Consistent || !afterCheck.Consistent
            ? "Unexpected: see the mismatch details above."
            : $"Before: audit and deletion disagree on {beforeCheck.Mismatches} booking(s). After: audit matches the {after.DeletedCount:N0} deleted rows exactly.");

        Ui.Compare("Measured (SQL commands captured by a DbCommandInterceptor)",
            ("SELECT / DELETE statements", $"{Count(beforeCapture, "SELECT") + Count(beforeCapture, "DELETE")}", $"{Count(afterCapture, "DELETE")}"),
            ("Audit INSERT batches (same in both)", $"{Count(beforeCapture, "INSERT")}", $"{Count(afterCapture, "INSERT")}"),
            ("Total round trips", $"{beforeCapture.RoundTrips}", $"{afterCapture.RoundTrips}"),
            ("Audit records written", $"{before.Audited.Count:N0}", $"{after.Audited.Count:N0}"),
            ("Rows actually deleted", $"{beforeCheck.ActuallyDeleted:N0}", $"{afterCheck.ActuallyDeleted:N0}"),
            ("Audited but NOT deleted", $"{beforeCheck.AuditedNotDeleted.Count}", $"{afterCheck.AuditedNotDeleted.Count}"),
            ("Deleted but NOT audited", $"{beforeCheck.DeletedNotAudited.Count}", $"{afterCheck.DeletedNotAudited.Count}"));

        // ---------------- SQL ----------------
        Ui.Sql(settings, "BEFORE, statement 1", First(beforeCapture, "SELECT"));
        Ui.Sql(settings, "BEFORE, statement 2", First(beforeCapture, "DELETE"));
        Ui.Sql(settings, "AFTER, the only read/delete statement", First(afterCapture, "DELETE"));
        Ui.SqlHint(settings);

        Ui.WhatChanged(
            "• The audited rows come from the DELETE itself (SQL Server: DELETE ... OUTPUT DELETED.[Id], ...), so nothing can change between 'what we read' and 'what we deleted'.\n" +
            "• Why the Before transaction didn't help: at READ COMMITTED, shared locks are released right after each row is read, and the DELETE re-evaluates its WHERE against whatever is committed by then. SERIALIZABLE or UPDLOCK/HOLDLOCK would also close the gap, by holding locks longer.\n" +
            "• The extension returns rows. Atomicity of 'delete + audit insert' still comes from our own transaction.");
    }

    /// <summary>"Another user" on a separate connection, auto-committed.</summary>
    public static async Task OtherUsersChangeBookingsAsync(DemoSettings settings, int extendId, int endEarlyId, DateTime cutoff)
    {
        await using var other = new BookingDbContext(settings, useReturningExtensions: false);
        await other.Bookings.Where(b => b.Id == extendId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.EndsAt, cutoff.AddDays(7)));
        await other.Bookings.Where(b => b.Id == endEarlyId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.EndsAt, cutoff.AddHours(-1)));
    }

    public record Verification(int ActuallyDeleted, IReadOnlyList<int> AuditedNotDeleted, IReadOnlyList<int> DeletedNotAudited, int AuditRows)
    {
        public int Mismatches => AuditedNotDeleted.Count + DeletedNotAudited.Count;
        public bool Consistent => Mismatches == 0 && AuditRows == ActuallyDeleted;
    }

    /// <summary>Checks the database itself: which bookings are really gone, and which audit rows exist.</summary>
    public static async Task<Verification> VerifyAsync(DemoSettings settings, IReadOnlyList<Booking> seeded, CleanupResult result)
    {
        await using var db = new BookingDbContext(settings, useReturningExtensions: false);
        var remaining = (await db.Bookings.Select(b => b.Id).ToListAsync()).ToHashSet();
        var actuallyDeleted = seeded.Select(b => b.Id).Where(id => !remaining.Contains(id)).ToHashSet();
        var audited = (await db.BookingAudits.Select(a => a.BookingId).ToListAsync()).ToHashSet();

        return new Verification(
            actuallyDeleted.Count,
            audited.Except(actuallyDeleted).Order().ToList(),
            actuallyDeleted.Except(audited).Order().ToList(),
            audited.Count);
    }

    private static void ShowMismatches(Verification v, bool after)
    {
        foreach (var id in v.AuditedNotDeleted)
            Ui.Bad($"Booking #{id} has an audit record saying it was deleted — but it still exists");
        foreach (var id in v.DeletedNotAudited)
            Ui.Bad($"Booking #{id} was deleted — but there is no audit record for it");
        if (v.Consistent)
            Ui.Good($"Audit records match the deleted rows exactly ({v.ActuallyDeleted:N0} = {v.AuditRows:N0})");
        else if (v.AuditRows == v.ActuallyDeleted)
            Ui.Bad($"Counts match ({v.AuditRows:N0} = {v.ActuallyDeleted:N0}), so a row-count check would NOT catch this");
    }

    private static int Count(SqlCapture c, string verb) => c.Commands.Count(cmd => Verb(cmd) == verb);
    private static string First(SqlCapture c, string verb) => c.Commands.FirstOrDefault(cmd => Verb(cmd) == verb) ?? "(not captured)";

    private static string Verb(string sql)
    {
        // EF prefixes batches with SET statements; find the first real verb.
        foreach (var line in sql.Split('\n').Select(l => l.Trim()))
            foreach (var verb in new[] { "SELECT", "DELETE", "INSERT", "MERGE", "UPDATE" })
                if (line.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                    return verb == "MERGE" ? "INSERT" : verb;
        return "OTHER";
    }
}
