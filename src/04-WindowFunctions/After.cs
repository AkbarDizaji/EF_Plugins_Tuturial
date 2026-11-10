using Microsoft.EntityFrameworkCore;
using Zomp.EFCore.WindowFunctions;

namespace Demo04.WindowFunctions;

/// <summary>
/// AFTER: SQL Server computes the running balance with
///   SUM(Amount) OVER (PARTITION BY AccountId ORDER BY CreatedAt, Id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
/// and only the requested page leaves the database.
/// </summary>
public static class After
{
    public static async Task<StatementPage> GetStatementPageAsync(BankDbContext db, int accountId, int page, int pageSize)
    {
        var lines = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new StatementLine(
                t.Id,
                t.CreatedAt,
                t.Kind,
                t.Amount,
                // Sum returns decimal?; the window always contains the current row, so it is never null
                (decimal)EF.Functions.Sum(
                    t.Amount,
                    EF.Functions.Over()
                        .PartitionBy(t.AccountId)
                        .OrderBy(t.CreatedAt).ThenBy(t.Id)
                        .Rows().FromUnbounded().ToCurrentRow())!))
            // OFFSET/FETCH is applied after the window function in SQL's logical order,
            // so the balance on page N still includes every row before it.
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new StatementPage(lines, RowsTransferred: lines.Count);
    }
}
