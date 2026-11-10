using Common;
using Demo03.Projectables;
using Microsoft.EntityFrameworkCore;

namespace EfCoreExtensionsDemo.Tests;

public class ProjectablesFixture : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(Seed.EnsureSeededAsync(TestSettings.Fast, force: false));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class ProjectablesTests : IClassFixture<ProjectablesFixture>
{
    private static readonly DemoSettings Settings = TestSettings.Fast;
    private const decimal MinTotal = ProjectablesDemo.MinTotal;

    [Fact]
    public async Task Before_PlainCalculatedProperty_IsNotTranslatable()
    {
        await using var db = new BeforeDbContext(Settings);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Before.UsingDomainPropertyAsync(db, MinTotal));
        Assert.Contains("could not be translated", ex.Message);
    }

    [Fact]
    public async Task After_MatchesDuplicatedFormula_AndInMemoryEvaluation()
    {
        List<OrderTotal> before, after, inMemory;
        await using (var db = new BeforeDbContext(Settings)) before = await Before.DuplicatedFormulaAsync(db, MinTotal);
        await using (var db = new AfterDbContext(Settings)) after = await After.ExpensiveOrdersAsync(db, MinTotal);
        await using (var db = new AfterDbContext(Settings))
            inMemory = (await db.Orders.AsNoTracking().Include(o => o.Items).ToListAsync())
                .Where(o => o.Total > MinTotal)
                .OrderByDescending(o => o.Total).ThenBy(o => o.Id)
                .Select(o => new OrderTotal(o.Id, o.Number, o.Total))
                .ToList();

        Assert.NotEmpty(after);
        Assert.Equal(before, after);   // same rows, same order, same exact decimals
        Assert.Equal(inMemory, after); // and the C# property agrees with SQL
    }

    [Fact]
    public async Task After_FilterAndSortAreTranslatedToSql()
    {
        var capture = new SqlCapture();
        int matching;
        await using (var db = new AfterDbContext(Settings, capture)) matching = (await After.ExpensiveOrdersAsync(db, MinTotal)).Count;

        var sql = Assert.Single(capture.Commands);
        var where = sql[sql.IndexOf("WHERE (", StringComparison.Ordinal)..];
        Assert.Contains("SUM([o0].[UnitPrice]", where);
        Assert.Contains("> @minTotal", where);
        Assert.Contains("DESC", sql[sql.IndexOf("ORDER BY", StringComparison.Ordinal)..]);
        Assert.True(matching < Seed.OrderCount(Settings)); // filtered in the database, not after loading everything
    }

    [Fact]
    public async Task After_GeneratesTheSameSqlAsTheHandWrittenCopy()
    {
        var beforeCapture = new SqlCapture();
        var afterCapture = new SqlCapture();
        await using (var db = new BeforeDbContext(Settings, beforeCapture)) await Before.DuplicatedFormulaAsync(db, MinTotal);
        await using (var db = new AfterDbContext(Settings, afterCapture)) await After.ExpensiveOrdersAsync(db, MinTotal);
        Assert.Equal(beforeCapture.Commands.Single(), afterCapture.Commands.Single());
    }

    [Fact]
    public void ProjectableProperty_StillWorksInMemory()
    {
        var order = new Order
        {
            DiscountRate = 0.10m,
            TaxRate = 0.20m,
            Items = [new() { UnitPrice = 100.00m, Quantity = 3 }, new() { UnitPrice = 12.50m, Quantity = 2 }]
        };
        Assert.Equal(325.00m, order.Subtotal);
        Assert.Equal(351.000000m, order.Total); // 325 × 0.9 × 1.2, exact
    }
}
