using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo04.WindowFunctions;

public sealed class WindowFunctionsDemo : IDemo
{
    public const int AccountId = 1;
    public const int PageSize = 50;

    public string Number => "04";
    public string Title => "Zomp EF Core Window Functions";

    public Task SeedAsync(DemoSettings settings, bool force = false) => Seed.EnsureSeededAsync(settings, force);

    public async Task ResetAsync(DemoSettings settings)
    {
        await using var db = new BankDbContext(settings, useWindowFunctions: false);
        await db.Database.EnsureDeletedAsync();
    }

    public async Task RunAsync(DemoSettings settings)
    {
        int historyLength;
        await using (var db = new BankDbContext(settings, useWindowFunctions: false))
            historyLength = await db.Transactions.CountAsync(t => t.AccountId == AccountId);
        var lastPage = (historyLength - 1) / PageSize;
        var page = lastPage / 2; // a page in the middle of the history

        Ui.Header(Number, Title);
        Ui.Problem($"An account statement shows a running balance beside each transaction. Account {AccountId} has " +
                   $"{historyLength:N0} transactions; the user opens page {page + 1} of {lastPage + 1} ({PageSize} rows per page).");

        // ---------------- BEFORE ----------------
        Ui.Before("Load the whole account history, compute the running balance in C#, keep one page");
        StatementPage before;
        TimeSpan beforeTime;
        await using (var db = new BankDbContext(settings, useWindowFunctions: false))
            (before, beforeTime) = await Measure.MedianAsync(() => Before.GetStatementPageAsync(db, AccountId, page, PageSize));
        Ui.Step(false, $"Rows transferred from SQL Server: {before.RowsTransferred:N0} to display {before.Lines.Count}");
        Ui.Step(false, $"Median time (5 runs after warm-up): {Measure.Format(beforeTime)}");
        Ui.Dim("Gets worse as the history grows: every page needs every earlier row, in memory, on the app server.");

        // ---------------- AFTER ----------------
        Ui.After("EF.Functions.Sum(t.Amount, EF.Functions.Over().PartitionBy(..).OrderBy(..).ThenBy(..).Rows().FromUnbounded().ToCurrentRow())");
        var capture = new SqlCapture();
        StatementPage after;
        TimeSpan afterTime;
        await using (var db = new BankDbContext(settings, useWindowFunctions: true, capture))
            (after, afterTime) = await Measure.MedianAsync(() => After.GetStatementPageAsync(db, AccountId, page, PageSize));
        Ui.Step(true, $"Rows transferred from SQL Server: {after.RowsTransferred:N0} to display {after.Lines.Count}");
        Ui.Step(true, $"Median time (5 runs after warm-up): {Measure.Format(afterTime)}");
        Ui.Dim("SQL Server still reads the earlier rows to compute the balance (via the covering index);");
        Ui.Dim("what disappears is shipping them over the network and adding them up on the app server.");

        // First rows of the statement: the example from the problem statement
        StatementPage firstPage;
        await using (var db = new BankDbContext(settings, useWindowFunctions: true))
            firstPage = await After.GetStatementPageAsync(db, AccountId, page: 0, PageSize);
        Ui.Table("Page 1 — first rows (computed by SQL Server)", ["Transaction", "Amount", "Balance"],
            firstPage.Lines.Take(4).Select(l => new[] { l.Kind, $"{l.Amount:+#,0.00;-#,0.00}", $"{l.Balance:#,0.00}" }));

        // Page boundary: balance must carry over from the previous page
        StatementPage previous;
        await using (var db = new BankDbContext(settings, useWindowFunctions: true))
            previous = await After.GetStatementPageAsync(db, AccountId, page - 1, PageSize);
        var lastOfPrevious = previous.Lines[^1];
        var firstOfPage = after.Lines[0];
        Ui.Table($"Page boundary {page} → {page + 1}", ["Row", "Transaction", "Amount", "Balance"],
        [
            [$"last of page {page}", lastOfPrevious.Kind, $"{lastOfPrevious.Amount:+#,0.00;-#,0.00}", $"{lastOfPrevious.Balance:#,0.00}"],
            [$"first of page {page + 1}", firstOfPage.Kind, $"{firstOfPage.Amount:+#,0.00;-#,0.00}", $"{firstOfPage.Balance:#,0.00}"],
        ]);
        if (lastOfPrevious.Balance + firstOfPage.Amount == firstOfPage.Balance)
            Ui.Good($"{lastOfPrevious.Balance:#,0.00} {firstOfPage.Amount:+#,0.00;-#,0.00} = {firstOfPage.Balance:#,0.00}: the balance carries across pages");
        else
            Ui.Bad("The balance does not carry across the page boundary.");

        // ---------------- RESULT ----------------
        var identical = before.Lines.SequenceEqual(after.Lines);
        Ui.Result(identical
            ? $"Both implementations returned the same {after.Lines.Count} statement lines with identical balances."
            : "MISMATCH between the C# and SQL running balances.");

        Ui.Compare($"Measured on page {page + 1} of account {AccountId}",
            ("Rows transferred from SQL Server", $"{before.RowsTransferred:N0}", $"{after.RowsTransferred:N0}"),
            ("Median time (approximate, 5 runs)", Measure.Format(beforeTime), Measure.Format(afterTime)),
            ("Statement lines identical", identical ? "yes" : "NO", identical ? "yes" : "NO"));

        Ui.Sql(settings, "AFTER — window function + OFFSET/FETCH", capture.Commands.Last());
        Ui.SqlHint(settings);

        Ui.WhatChanged(
            "SQL Server has supported SUM(...) OVER (...) natively since 2012; EF Core just has no LINQ for it. " +
            "Zomp.EFCore.WindowFunctions adds EF.Functions.Sum/RowNumber/Lag/... with Over(), so the running balance " +
            "is computed where the data lives, ordered by CreatedAt then Id so ties are deterministic, " +
            "and paging happens after the balance is calculated.");
    }
}
