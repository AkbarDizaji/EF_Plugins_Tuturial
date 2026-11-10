using Microsoft.EntityFrameworkCore;

namespace Demo04.WindowFunctions;

/// <summary>
/// BEFORE: a balance depends on every earlier transaction, so to show page N we load the whole
/// account history into memory, add it up in C#, and throw away everything except one page.
/// </summary>
public static class Before
{
    public static async Task<StatementPage> GetStatementPageAsync(BankDbContext db, int accountId, int page, int pageSize)
    {
        var history = await db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id) // deterministic: Id breaks ties
            .Select(t => new { t.Id, t.CreatedAt, t.Kind, t.Amount })
            .ToListAsync();

        var lines = new List<StatementLine>(history.Count);
        var balance = 0m;
        foreach (var t in history)
        {
            balance += t.Amount;
            lines.Add(new StatementLine(t.Id, t.CreatedAt, t.Kind, t.Amount, balance));
        }

        return new StatementPage(lines.Skip(page * pageSize).Take(pageSize).ToList(), RowsTransferred: history.Count);
    }
}
