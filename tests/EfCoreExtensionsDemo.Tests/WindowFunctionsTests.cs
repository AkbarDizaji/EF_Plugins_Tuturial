using Common;
using Demo04.WindowFunctions;
using Microsoft.EntityFrameworkCore;

namespace EfCoreExtensionsDemo.Tests;

public class WindowFunctionsFixture : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(Seed.EnsureSeededAsync(TestSettings.Fast, force: false));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class WindowFunctionsTests : IClassFixture<WindowFunctionsFixture>
{
    private static readonly DemoSettings Settings = TestSettings.Fast;
    private const int PageSize = WindowFunctionsDemo.PageSize;

    /// <summary>The whole statement computed in C# (page size = everything).</summary>
    private static async Task<IReadOnlyList<StatementLine>> FullStatementInMemoryAsync(int accountId)
    {
        await using var db = new BankDbContext(Settings, useWindowFunctions: false);
        return (await Before.GetStatementPageAsync(db, accountId, page: 0, pageSize: int.MaxValue)).Lines;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task EveryPage_MatchesTheInMemoryRunningBalance(int accountId)
    {
        var expected = await FullStatementInMemoryAsync(accountId);
        var pages = (expected.Count + PageSize - 1) / PageSize;
        Assert.True(pages > 2);

        await using var db = new BankDbContext(Settings, useWindowFunctions: true);
        for (var page = 0; page < pages; page++)
        {
            var actual = await After.GetStatementPageAsync(db, accountId, page, PageSize);
            Assert.Equal(expected.Skip(page * PageSize).Take(PageSize), actual.Lines);
            Assert.Equal(actual.Lines.Count, actual.RowsTransferred);
        }
    }

    [Fact]
    public async Task Balance_CarriesAcrossEveryPageBoundary()
    {
        await using var db = new BankDbContext(Settings, useWindowFunctions: true);
        StatementLine? lastOfPrevious = null;
        for (var page = 0; ; page++)
        {
            var lines = (await After.GetStatementPageAsync(db, 1, page, PageSize)).Lines;
            if (lines.Count == 0) break;
            if (lastOfPrevious is not null)
                Assert.Equal(lastOfPrevious.Balance + lines[0].Amount, lines[0].Balance);
            for (var i = 1; i < lines.Count; i++)
                Assert.Equal(lines[i - 1].Balance + lines[i].Amount, lines[i].Balance);
            lastOfPrevious = lines[^1];
        }
        Assert.NotNull(lastOfPrevious);
    }

    [Fact]
    public async Task BeforeAndAfter_ReturnIdenticalPages()
    {
        StatementPage before, after;
        await using (var db = new BankDbContext(Settings, useWindowFunctions: false)) before = await Before.GetStatementPageAsync(db, 1, 9, PageSize);
        await using (var db = new BankDbContext(Settings, useWindowFunctions: true)) after = await After.GetStatementPageAsync(db, 1, 9, PageSize);

        Assert.Equal(before.Lines, after.Lines);
        Assert.Equal(PageSize, after.RowsTransferred);
        Assert.True(before.RowsTransferred > after.RowsTransferred);
    }

    [Fact]
    public async Task OpeningRows_MatchTheExampleStatement()
    {
        await using var db = new BankDbContext(Settings, useWindowFunctions: true);
        var lines = (await After.GetStatementPageAsync(db, 1, 0, PageSize)).Lines.Take(4);
        Assert.Equal(
            [("Deposit", 1000m, 1000m), ("Purchase", -300m, 700m), ("Withdrawal", -200m, 500m), ("Deposit", 600m, 1100m)],
            lines.Select(l => (l.Kind, l.Amount, l.Balance)));
    }

    [Fact]
    public async Task GeneratedSql_UsesTheWindowFunction_AndPagesInTheDatabase()
    {
        var capture = new SqlCapture();
        await using var db = new BankDbContext(Settings, useWindowFunctions: true, capture);
        await After.GetStatementPageAsync(db, 1, 3, PageSize);

        var sql = Assert.Single(capture.Commands);
        Assert.Contains("SUM([t].[Amount]) OVER(PARTITION BY [t].[AccountId] ORDER BY [t].[CreatedAt], [t].[Id] ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)", sql);
        Assert.Contains("OFFSET @p ROWS FETCH NEXT", sql);
    }

    [Fact]
    public async Task Data_ContainsTimestampTies_SoTheIdTieBreakerMatters()
    {
        await using var db = new BankDbContext(Settings, useWindowFunctions: false);
        var ties = await db.Transactions
            .GroupBy(t => new { t.AccountId, t.CreatedAt })
            .CountAsync(g => g.Count() > 1);
        Assert.True(ties > 0);
    }
}
