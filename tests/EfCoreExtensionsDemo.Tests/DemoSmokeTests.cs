using Common;

namespace EfCoreExtensionsDemo.Tests;

/// <summary>Each demo runs end to end (seed + Before + After + output) without throwing.</summary>
[Collection("Smoke")] // run one at a time: they write to the console
public class DemoSmokeTests
{
    public static TheoryData<string> Demos => ["01", "02", "03", "04"];

    [Theory]
    [MemberData(nameof(Demos))]
    public async Task Demo_RunsEndToEnd(string number)
    {
        IDemo demo = number switch
        {
            "01" => new Demo01.Returning.ReturningDemo(),
            "02" => new Demo02.QueryComplexity.QueryComplexityDemo(),
            "03" => new Demo03.Projectables.ProjectablesDemo(),
            _ => new Demo04.WindowFunctions.WindowFunctionsDemo(),
        };
        var settings = TestSettings.Fast with { ShowSql = true, DatabaseSuffix = "_Smoke" };
        await demo.SeedAsync(settings);
        await demo.RunAsync(settings);
    }
}
