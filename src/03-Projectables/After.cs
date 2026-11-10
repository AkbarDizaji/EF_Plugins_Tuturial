using Microsoft.EntityFrameworkCore;

namespace Demo03.Projectables;

public static class After
{
    /// <summary>The query we wanted to write in the first place. Order.Total is expanded into SQL.</summary>
    public static Task<List<OrderTotal>> ExpensiveOrdersAsync(AfterDbContext db, decimal minTotal) =>
        db.Orders
            .Where(o => o.Total > minTotal)
            .OrderByDescending(o => o.Total)
            .ThenBy(o => o.Id)
            .Select(o => new OrderTotal(o.Id, o.Number, o.Total))
            .ToListAsync();
}
