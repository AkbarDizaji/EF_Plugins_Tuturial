using Microsoft.EntityFrameworkCore;

namespace Demo03.Projectables;

public static class Before
{
    /// <summary>
    /// Attempt 1: use the domain property in the query. EF Core can't translate PlainOrder.Total
    /// (it's compiled C#, not an expression tree), so this throws InvalidOperationException.
    /// </summary>
    public static Task<List<PlainOrder>> UsingDomainPropertyAsync(BeforeDbContext db, decimal minTotal) =>
        db.Orders
            .Where(o => o.Total > minTotal)
            .OrderByDescending(o => o.Total)
            .ToListAsync();

    /// <summary>
    /// Attempt 2 (the usual workaround): copy the formula into the query. Now it lives in two places.
    /// </summary>
    public static Task<List<OrderTotal>> DuplicatedFormulaAsync(BeforeDbContext db, decimal minTotal) =>
        db.Orders
            .Select(o => new
            {
                o.Id,
                o.Number,
                // ⚠ copy of PlainOrder.Total — must be kept in sync by hand
                Total = o.Items.Sum(i => i.UnitPrice * i.Quantity) * (1 - o.DiscountRate) * (1 + o.TaxRate)
            })
            .Where(o => o.Total > minTotal)
            .OrderByDescending(o => o.Total)
            .ThenBy(o => o.Id)
            .Select(o => new OrderTotal(o.Id, o.Number, o.Total))
            .ToListAsync();
}
