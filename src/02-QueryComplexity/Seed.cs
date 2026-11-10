using Common;
using Microsoft.EntityFrameworkCore;

namespace Demo02.QueryComplexity;

public static class Seed
{
    public static int OrderCount(DemoSettings s) => s.Scale(1_000, 10_000, 50_000);

    private static readonly string[] Statuses = ["Created", "Paid", "Packed", "Shipped", "Delivered", "Returned"];

    public static async Task EnsureSeededAsync(DemoSettings settings, bool force)
    {
        await using var db = new ReportingDbContext(settings, protect: false);
        var expected = OrderCount(settings);
        if (!force && await db.Database.CanConnectAsync()
            && await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Orders]").SingleAsync() == expected)
            return;

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var random = new Random(42); // deterministic
        var start = new DateTime(2025, 1, 1);
        var orders = new List<Order>();
        var items = new List<OrderItem>();
        var payments = new List<Payment>();
        var history = new List<OrderHistory>();

        for (var id = 1; id <= expected; id++)
        {
            var created = start.AddMinutes(random.Next(0, 525_600));
            orders.Add(new Order { Id = id, CustomerId = random.Next(1, 501), CreatedAt = created, Status = Statuses[random.Next(Statuses.Length)] });
            for (var i = random.Next(3, 9); i > 0; i--)
                items.Add(new OrderItem { Id = items.Count + 1, OrderId = id, Sku = $"SKU-{random.Next(1, 2000):D4}", Quantity = random.Next(1, 5), UnitPrice = random.Next(500, 50_000) / 100m });
            for (var p = random.Next(1, 4); p > 0; p--)
                payments.Add(new Payment { Id = payments.Count + 1, OrderId = id, Amount = random.Next(1_000, 100_000) / 100m, PaidAt = created.AddHours(p) });
            for (var h = random.Next(2, 7); h > 0; h--)
                history.Add(new OrderHistory { Id = history.Count + 1, OrderId = id, ChangedAt = created.AddHours(h * 6), Status = Statuses[random.Next(Statuses.Length)] });
        }

        var cs = settings.ConnectionStringFor(ReportingDbContext.DatabaseName);
        await BulkInsert.WriteAsync(cs, "Orders", orders);
        await BulkInsert.WriteAsync(cs, "OrderItems", items);
        await BulkInsert.WriteAsync(cs, "Payments", payments);
        await BulkInsert.WriteAsync(cs, "OrderHistory", history);
    }
}
