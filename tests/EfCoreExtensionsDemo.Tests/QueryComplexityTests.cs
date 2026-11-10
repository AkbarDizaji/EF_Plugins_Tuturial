using Common;
using Demo02.QueryComplexity;
using EfQueryComplexity;

namespace EfCoreExtensionsDemo.Tests;

public class QueryComplexityFixture : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(Seed.EnsureSeededAsync(TestSettings.Fast, force: false));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class QueryComplexityTests : IClassFixture<QueryComplexityFixture>
{
    private static readonly DemoSettings Settings = TestSettings.Fast;

    private static async Task<QueryComplexityException> RejectedAsync(Func<ReportingDbContext, Task> query, SqlCapture? capture = null)
    {
        await using var db = new ReportingDbContext(Settings, protect: true, capture);
        return await Assert.ThrowsAsync<QueryComplexityException>(() => query(db));
    }

    [Fact]
    public async Task Unbounded_IsRejected()
    {
        var ex = await RejectedAsync(After.AllOrdersAsync);
        var v = Assert.Single(ex.Violations);
        Assert.Equal("RejectUnbounded", v.Limit);
        Assert.Contains(typeof(Order), v.RowTypes!);
    }

    [Fact]
    public async Task BeforeExport_ViolatesIncludesCollectionsAndUnbounded()
    {
        var ex = await RejectedAsync(After.FullExportAsync);
        Assert.Equal(["MaxIncludes", "MaxSingleQueryCollections", "RejectUnbounded"], ex.Violations.Select(v => v.Limit).Order());
    }

    [Fact]
    public async Task ThreeIncludes_IsRejectedByIncludeLimitOnly()
    {
        var ex = await RejectedAsync(After.ThreeIncludesAsync);
        var v = Assert.Single(ex.Violations);
        Assert.Equal(("MaxIncludes", 2, 3), (v.Limit, v.Max, v.Actual));
    }

    [Fact]
    public async Task TakeAboveLimit_IsRejected_AndWithinLimitIsAllowed()
    {
        var ex = await RejectedAsync(db => After.HugePageAsync(db, 5000));
        Assert.Equal(("MaxTake", 1000, 5000), (ex.Violations[0].Limit, ex.Violations[0].Max, ex.Violations[0].Actual));

        await using var db = new ReportingDbContext(Settings, protect: true);
        Assert.Equal(1000, (await After.HugePageAsync(db, 1000)).Count);
    }

    [Fact]
    public async Task DeepDynamicFilter_IsRejectedByMaxDepth()
    {
        var ex = await RejectedAsync(db => After.DynamicFilterAsync(db, Enumerable.Range(1, 40)));
        var v = Assert.Single(ex.Violations);
        Assert.Equal("MaxDepth", v.Limit);
        Assert.True(v.Actual > v.Max);
    }

    [Fact]
    public async Task SafePaginatedQuery_Succeeds_WithIncludesLoaded()
    {
        await using var db = new ReportingDbContext(Settings, protect: true);
        var page = await After.SafePageAsync(db, page: 3);

        Assert.Equal(100, page.Count);
        Assert.Equal(Enumerable.Range(301, 100), page.Select(o => o.Id));
        Assert.All(page, o => Assert.NotEmpty(o.Items));
        Assert.All(page, o => Assert.NotEmpty(o.Payments));
    }

    [Fact]
    public async Task RejectedQueries_SendNoSqlToTheDatabase()
    {
        var capture = new SqlCapture();
        await RejectedAsync(After.AllOrdersAsync, capture);
        await RejectedAsync(After.ThreeIncludesAsync, capture);
        await RejectedAsync(db => After.HugePageAsync(db, 5000), capture);
        Assert.Empty(capture.Commands);
    }

    [Fact]
    public async Task UnprotectedContext_RunsTheExpensiveQuery_WithCartesianRows()
    {
        var capture = new SqlCapture();
        await using var db = new ReportingDbContext(Settings, protect: false, capture);
        var orders = await Before.SingleQueryAsync(db);

        Assert.Equal(Seed.OrderCount(Settings), orders.Count);
        var sql = Assert.Single(capture.Commands);
        Assert.Equal(3, sql.Split("LEFT JOIN").Length - 1);
    }
}
