using Common;
using EfQueryComplexity;
using Microsoft.EntityFrameworkCore;

namespace Demo02.QueryComplexity;

public sealed class QueryComplexityDemo : IDemo
{
    public string Number => "02";
    public string Title => "EfQueryComplexity";

    public Task SeedAsync(DemoSettings settings, bool force = false) => Seed.EnsureSeededAsync(settings, force);

    public async Task ResetAsync(DemoSettings settings)
    {
        await using var db = new ReportingDbContext(settings, protect: false);
        await db.Database.EnsureDeletedAsync();
    }

    public async Task RunAsync(DemoSettings settings)
    {
        Ui.Header(Number, Title);
        Ui.Problem(
            "Customers build their own reports. One \"export everything\" report loads orders with items, payments " +
            "and history — three collection Includes, no paging. EF Core runs it without complaint.");

        // ---------------- BEFORE ----------------
        Ui.Before("db.Orders.Include(Items).Include(Payments).Include(History).ToListAsync()");
        var capture = new SqlCapture();
        List<Order> orders;
        TimeSpan singleTime;
        await using (var db = new ReportingDbContext(settings, protect: false, capture))
            (orders, singleTime) = await Ui.StatusAsync("Running the unbounded single query...",
                () => Measure.OnceAsync(() => Before.SingleQueryAsync(db)));
        var singleSql = capture.Commands.Single();

        long sqlRows;
        await using (var db = new ReportingDbContext(settings, protect: false))
            sqlRows = await Ui.StatusAsync("Counting the rows that SQL returned...", () => CountRowsAsync(db, singleSql));

        var entities = orders.Count + orders.Sum(o => o.Items.Count + o.Payments.Count + o.History.Count);
        Ui.Step(false, $"Loaded {orders.Count:N0} orders and {entities - orders.Count:N0} child rows = {entities:N0} entities");
        Ui.Step(false, $"SQL Server returned {sqlRows:N0} rows to build them ({(double)sqlRows / entities:N1}x more rows than entities)");
        Ui.Step(false, $"Single run: {Measure.Format(singleTime)}");
        Ui.Dim("Why: Items × Payments × History are LEFT JOINed side by side, so each order repeats once per combination.");

        capture.Reset();
        TimeSpan splitTime;
        await using (var db = new ReportingDbContext(settings, protect: false, capture))
            (_, splitTime) = await Ui.StatusAsync("Running the same query with AsSplitQuery()...",
                () => Measure.OnceAsync(() => Before.SplitQueryAsync(db)));
        Ui.Step(false, $"With AsSplitQuery(): {capture.RoundTrips} SQL queries returning {entities:N0} rows in total, single run {Measure.Format(splitTime)}");
        Ui.Dim("Split queries fix the multiplication, but nothing forces a developer — or a customer's report — to use them,");
        Ui.Dim("and the query is still unbounded: it returns every order in the table.");

        Ui.Sql(settings, "BEFORE, single query (note the three LEFT JOINs)", singleSql);

        // ---------------- AFTER ----------------
        Ui.After("Same queries, context configured with UseQueryComplexity(logAt: limits, throwAt: limits)");
        var l = ReportingDbContext.ReportingLimits;
        Ui.Dim($"Limits: MaxTake {l.MaxTake}, MaxIncludes {l.MaxIncludes}, MaxDepth {l.MaxDepth}, " +
               $"MaxSingleQueryCollections {l.MaxSingleQueryCollections}, RejectUnbounded");

        var customerIds = Enumerable.Range(1, 40).ToList();
        var cases = new (string Query, Func<ReportingDbContext, Task<List<Order>>> Run)[]
        {
            ("db.Orders.ToListAsync()", db => After.AllOrdersAsync(db)),
            ("The BEFORE export (3 Includes, no Take)", db => After.FullExportAsync(db)),
            ("3 Includes + AsSplitQuery + Take(100)", db => After.ThreeIncludesAsync(db)),
            ("OrderBy(Id).Take(5000)", db => After.HugePageAsync(db, pageSize: 5000)),
            ($"Report filter: {customerIds.Count} customers OR'ed together", db => After.DynamicFilterAsync(db, customerIds)),
            ("Page 3: Skip(300).Take(100) + 2 Includes + AsSplitQuery", db => After.SafePageAsync(db, page: 3)),
        };

        var rows = new List<string[]>();
        QueryComplexityException? firstRejection = null;
        var commandsSentByRejected = 0;
        var protectedCapture = new SqlCapture();
        await using (var db = new ReportingDbContext(settings, protect: true, protectedCapture))
        {
            foreach (var (query, run) in cases)
            {
                protectedCapture.Reset();
                try
                {
                    var page = await run(db);
                    rows.Add([query, $"✔ {page.Count} orders, {page.Sum(o => o.Items.Count + o.Payments.Count)} children", "—"]);
                }
                catch (QueryComplexityException ex)
                {
                    firstRejection ??= ex;
                    commandsSentByRejected += protectedCapture.RoundTrips;
                    rows.Add([query, "✘ rejected", string.Join("\n", ex.Violations.Select(Describe))]);
                }
            }
        }
        Ui.Table("Report queries against the protected context", ["Query", "Result", "Rule violated (actual > max)"], rows);

        if (firstRejection is not null)
        {
            Ui.Dim($"{nameof(QueryComplexityException)}.Message (first rejection, query text omitted):");
            foreach (var line in firstRejection.Message.Split('\n').Select(x => x.TrimEnd()).TakeWhile(x => x.Length > 0 && !x.StartsWith("Query:")))
                Ui.Dim("  " + line);
        }

        // ---------------- RESULT ----------------
        var rejected = rows.Count(r => r[1].StartsWith('✘'));
        Ui.Result($"{rejected} of {rows.Count} report queries were rejected; the paginated one ran. " +
                  $"SQL commands sent by the rejected queries: {commandsSentByRejected} (counted by an interceptor).");

        Ui.WhatChanged(
            "• EfQueryComplexity checks the SHAPE of each LINQ query when it is compiled (and Take values on every execution) and throws QueryComplexityException naming each violated rule.\n" +
            "• It does not predict what the SQL will cost: an allowed query over an unindexed column can still be slow.\n" +
            "• Related but different tools: AsSplitQuery avoids the row multiplication, pagination bounds the result, indexes make allowed queries fast, and database monitoring (Query Store, timeouts) catches what slips through.");
    }

    /// <summary>Executes the exact SQL EF Core generated and counts the rows it returns.</summary>
    private static async Task<long> CountRowsAsync(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;
        await using var reader = await command.ExecuteReaderAsync();
        long count = 0;
        while (await reader.ReadAsync()) count++;
        return count;
    }

    private static string Describe(QueryComplexityViolation v) =>
        v.Max is null && v.Actual is null
            ? $"{v.Limit} ({string.Join(", ", (v.RowTypes ?? []).Select(t => t.Name))} rows without Take)"
            : $"{v.Limit}: {v.Actual} > {v.Max}";
}
