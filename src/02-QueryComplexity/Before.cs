using Microsoft.EntityFrameworkCore;

namespace Demo02.QueryComplexity;

/// <summary>
/// BEFORE: the reporting API builds whatever query the customer asks for. Nothing stops this one.
/// </summary>
public static class Before
{
    /// <summary>"Export all orders with items, payments and history."</summary>
    public static IQueryable<Order> FullExport(ReportingDbContext db) =>
        db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.History);

    /// <summary>One SQL query with three LEFT JOINs: every item × every payment × every history row, per order.</summary>
    public static Task<List<Order>> SingleQueryAsync(ReportingDbContext db) =>
        FullExport(db).ToListAsync();

    /// <summary>AsSplitQuery fixes the row multiplication (4 queries instead of 1) — but it is still unbounded.</summary>
    public static Task<List<Order>> SplitQueryAsync(ReportingDbContext db) =>
        FullExport(db).AsSplitQuery().ToListAsync();
}
