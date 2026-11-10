using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo03.Projectables;

public static class Seed
{
    public static int OrderCount(DemoSettings s) => s.Scale(1_000, 10_000, 100_000);

    private static readonly decimal[] Discounts = [0.00m, 0.00m, 0.05m, 0.10m, 0.15m, 0.25m];
    private static readonly decimal[] TaxRates = [0.07m, 0.10m, 0.19m, 0.20m, 0.25m];

    public static async Task EnsureSeededAsync(DemoSettings settings, bool force)
    {
        await using var db = new AfterDbContext(settings);
        var expected = OrderCount(settings);
        if (!force && await db.Database.CanConnectAsync()
            && await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Orders]").SingleAsync() == expected)
            return;

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var random = new Random(7); // deterministic
        var orders = new List<Order>();
        var items = new List<OrderItem>();
        for (var id = 1; id <= expected; id++)
        {
            orders.Add(new Order
            {
                Id = id,
                Number = $"SO-{id:D6}",
                DiscountRate = Discounts[random.Next(Discounts.Length)],
                TaxRate = TaxRates[random.Next(TaxRates.Length)]
            });
            for (var i = random.Next(1, 7); i > 0; i--)
                items.Add(new OrderItem
                {
                    Id = items.Count + 1,
                    OrderId = id,
                    UnitPrice = random.Next(199, 40_000) / 100m, // 1.99 – 399.99
                    Quantity = random.Next(1, 5)
                });
        }

        var cs = settings.ConnectionStringFor(ShopDbContextBase.DatabaseName);
        await BulkInsert.WriteAsync(cs, "Orders", orders);
        await BulkInsert.WriteAsync(cs, "OrderItems", items);
    }
}
