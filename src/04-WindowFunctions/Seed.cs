using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo04.WindowFunctions;

public static class Seed
{
    public const int AccountCount = 10;

    public static int TransactionCount(DemoSettings s) => s.Scale(10_000, 100_000, 1_000_000);

    /// <summary>Every account starts with the example from the video: +1000, -300, -200, +600.</summary>
    private static readonly (string Kind, decimal Amount)[] Opening =
        [("Deposit", 1000m), ("Purchase", -300m), ("Withdrawal", -200m), ("Deposit", 600m)];

    public static async Task EnsureSeededAsync(DemoSettings settings, bool force)
    {
        await using var db = new BankDbContext(settings, useWindowFunctions: false);
        var expected = TransactionCount(settings);
        if (!force && await db.Database.CanConnectAsync()
            && await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Transactions]").SingleAsync() == expected)
            return;

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var random = new Random(1234); // deterministic
        var accounts = Enumerable.Range(1, AccountCount).Select(id => new Account { Id = id, Name = $"Account {id:D2}" }).ToList();
        var transactions = new List<Transaction>(expected);
        var perAccount = expected / AccountCount;

        foreach (var account in accounts)
        {
            var at = new DateTime(2024, 1, 1, 9, 0, 0);
            for (var i = 0; i < perAccount; i++)
            {
                // ~15% of transactions share the previous timestamp → ordering by CreatedAt alone is NOT deterministic
                if (i < Opening.Length || random.Next(100) >= 15)
                    at = at.AddMinutes(random.Next(1, 600));

                var (kind, amount) = i < Opening.Length ? Opening[i] : Next(random);
                transactions.Add(new Transaction { AccountId = account.Id, CreatedAt = at, Kind = kind, Amount = amount });
            }
        }

        // Ids are a deterministic shuffle: in a real ledger, insertion order ≠ business order (back-dated entries).
        // Keep the four opening rows' Ids ascending so ties among them can't reorder the example.
        var ids = Enumerable.Range(1, transactions.Count).ToArray();
        random.Shuffle(ids);
        for (var i = 0; i < transactions.Count; i++) transactions[i].Id = ids[i];
        foreach (var group in transactions.GroupBy(t => t.AccountId))
        {
            var opening = group.Take(Opening.Length).ToList();
            var sortedIds = opening.Select(t => t.Id).Order().ToList();
            for (var i = 0; i < opening.Count; i++) opening[i].Id = sortedIds[i];
        }

        var cs = settings.ConnectionStringFor(BankDbContext.DatabaseName);
        await BulkInsert.WriteAsync(cs, "Accounts", accounts);
        await BulkInsert.WriteAsync(cs, "Transactions", transactions);
    }

    private static (string, decimal) Next(Random random) => random.Next(10) switch
    {
        < 3 => ("Deposit", random.Next(5_000, 87_000) / 100m), // inflows ≈ outflows on average
        < 8 => ("Purchase", -random.Next(100, 40_000) / 100m),
        _ => ("Withdrawal", -random.Next(2_000, 30_000) / 100m)
    };
}
