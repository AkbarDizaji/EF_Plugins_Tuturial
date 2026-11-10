using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Demo02.QueryComplexity;

/// <summary>
/// AFTER: the same kinds of report queries, run against a context with UseQueryComplexity(...).
/// The code is unchanged — the guardrail lives in the DbContext configuration.
/// </summary>
public static class After
{
    // ❌ No Take → RejectUnbounded
    public static Task<List<Order>> AllOrdersAsync(ReportingDbContext db) =>
        db.Orders.ToListAsync();

    // ❌ The exact BEFORE query → several rules at once
    public static Task<List<Order>> FullExportAsync(ReportingDbContext db) =>
        Before.FullExport(db).ToListAsync();

    // ❌ Paginated and split, but 3 Includes → MaxIncludes (limit 2)
    public static Task<List<Order>> ThreeIncludesAsync(ReportingDbContext db) =>
        db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.History)
            .AsSplitQuery()
            .OrderBy(o => o.Id)
            .Take(100)
            .ToListAsync();

    // ❌ "Page size" from the query string is 5000 → MaxTake (checked on every execution)
    public static Task<List<Order>> HugePageAsync(ReportingDbContext db, int pageSize) =>
        db.Orders.OrderBy(o => o.Id).Take(pageSize).ToListAsync();

    // ❌ A report builder that ORs together one condition per selected customer → MaxDepth
    public static Task<List<Order>> DynamicFilterAsync(ReportingDbContext db, IEnumerable<int> customerIds) =>
        db.Orders.Where(AnyCustomer(customerIds)).OrderBy(o => o.Id).Take(100).ToListAsync();

    // ✅ Paginated, two Includes, split query → allowed
    public static Task<List<Order>> SafePageAsync(ReportingDbContext db, int page, int pageSize = 100) =>
        db.Orders
            .OrderBy(o => o.Id)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .AsSplitQuery()
            .ToListAsync();

    /// <summary>o => o.CustomerId == a || o.CustomerId == b || ... — each OR nests one level deeper.</summary>
    public static Expression<Func<Order, bool>> AnyCustomer(IEnumerable<int> customerIds)
    {
        var o = Expression.Parameter(typeof(Order), "o");
        var customerId = Expression.Property(o, nameof(Order.CustomerId));
        var body = customerIds
            .Select(id => (Expression)Expression.Equal(customerId, Expression.Constant(id)))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<Order, bool>>(body, o);
    }
}
