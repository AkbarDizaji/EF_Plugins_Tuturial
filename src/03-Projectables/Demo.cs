using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo03.Projectables;

public sealed class ProjectablesDemo : IDemo
{
    public const decimal MinTotal = 1000m;

    public string Number => "03";
    public string Title => "Projectables";

    public Task SeedAsync(DemoSettings settings, bool force = false) => Seed.EnsureSeededAsync(settings, force);

    public async Task ResetAsync(DemoSettings settings)
    {
        await using var db = new AfterDbContext(settings);
        await db.Database.EnsureDeletedAsync();
    }

    public async Task RunAsync(DemoSettings settings)
    {
        Ui.Header(Number, Title);
        Ui.Problem("Duplicated financial business logic. The order total is needed in C# (invoices) AND in queries (reports).");
        Ui.Info("Domain property:");
        Ui.Dim("public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity) * (1 - DiscountRate) * (1 + TaxRate);");
        Ui.Dim("Assumptions: prices 2 dp, rates 2 dp, Total exact at 6 dp, rounding to cents only for display.");

        // ---------------- BEFORE ----------------
        Ui.Before("Implementation without extension");
        await using (var db = new BeforeDbContext(settings))
        {
            try
            {
                await Before.UsingDomainPropertyAsync(db, MinTotal);
                Ui.Bad("Unexpected: the plain property was translated.");
            }
            catch (InvalidOperationException ex)
            {
                Ui.Step(false, "db.Orders.Where(o => o.Total > 1000) →  InvalidOperationException:");
                Ui.Dim(FirstSentence(ex.Message));
            }
        }

        var beforeCapture = new SqlCapture();
        List<OrderTotal> before;
        await using (var db = new BeforeDbContext(settings, beforeCapture))
            before = await Before.DuplicatedFormulaAsync(db, MinTotal);
        Ui.Step(false, $"Workaround: formula copied into the LINQ query → {before.Count:N0} orders");
        Ui.Dim("Two copies of one financial rule. Change the domain formula (e.g. discount after tax, or a shipping fee)");
        Ui.Dim("and forget the query copy: the invoice and the report now disagree — silently, with no compile error.");

        // ---------------- AFTER ----------------
        Ui.After("[Projectable] on Order.Total + options.UseProjectables()");
        var afterCapture = new SqlCapture();
        List<OrderTotal> after;
        await using (var db = new AfterDbContext(settings, afterCapture))
            after = await After.ExpensiveOrdersAsync(db, MinTotal);
        Ui.Step(true, $"db.Orders.Where(o => o.Total > 1000).OrderByDescending(o => o.Total) → {after.Count:N0} orders");
        var afterSql = afterCapture.Commands.Last();
        var beforeSql = beforeCapture.Commands.Last();
        if (afterSql == beforeSql)
            Ui.Good("The generated SQL is character-for-character identical to the hand-copied version");
        var filteredInSql = afterSql.Contains("WHERE") && afterSql.Contains("SUM(");
        if (filteredInSql) Ui.Good("Filter and sort run in SQL Server (WHERE / ORDER BY contain the SUM) — only matching rows are transferred");
        else Ui.Bad("The generated SQL does not contain the formula — check the SQL below.");

        // Ground truth: evaluate the very same C# property in memory over every order.
        List<OrderTotal> inMemory;
        await using (var db = new AfterDbContext(settings))
            inMemory = (await db.Orders.AsNoTracking().Include(o => o.Items).ToListAsync())
                .Where(o => o.Total > MinTotal)
                .OrderByDescending(o => o.Total).ThenBy(o => o.Id)
                .Select(o => new OrderTotal(o.Id, o.Number, o.Total))
                .ToList();

        Ui.Table($"Top 5 orders over {MinTotal:N0} (rounded to cents for display only)", ["Order", "Before (copy)", "After (projectable)", "C# in memory"],
            after.Take(5).Select((a, i) => new[]
            {
                a.Number,
                Cents(before[i].Total),
                Cents(a.Total),
                Cents(inMemory[i].Total)
            }));

        // ---------------- RESULT ----------------
        var identical = before.SequenceEqual(after) && after.SequenceEqual(inMemory);
        Ui.Result(identical
            ? $"Both implementations returned {after.Count:N0} matching orders, with identical totals (exact decimals, same order) — " +
              "and identical to evaluating Order.Total in C#."
            : $"MISMATCH: before {before.Count:N0}, after {after.Count:N0}, in-memory {inMemory.Count:N0}.");

        Ui.Sql(settings, "BEFORE (formula duplicated by hand)", beforeSql);
        Ui.Sql(settings, "AFTER (formula expanded from [Projectable] Order.Total)", afterSql);
        Ui.SqlHint(settings);

        Ui.WhatChanged(
            "The calculated property is now translated to SQL. A source generator emits an expression tree for each " +
            "[Projectable] member, and UseProjectables() inlines it before EF Core translates the query. " +
            "One formula, used by C# code and by SQL — so the two can't drift apart.");
    }

    private static string Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("N2");

    private static string FirstSentence(string message)
    {
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? message[..(end + 1)] : message;
    }
}
