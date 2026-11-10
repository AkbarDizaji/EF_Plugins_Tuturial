namespace Common;

/// <summary>Entry point shared by each demo's Program.cs: seed, reset or run one demo.</summary>
public static class DemoHost
{
    public static async Task<int> RunAsync(IDemo demo, string[] args)
    {
        var settings = DemoSettings.Parse(args);
        var command = args.FirstOrDefault(a => !a.StartsWith("--"))?.ToLowerInvariant() ?? "run";
        try
        {
            switch (command)
            {
                case "seed":  await demo.SeedAsync(settings, force: true); break;
                case "reset": await demo.ResetAsync(settings); break;
                default:
                    await demo.SeedAsync(settings);
                    await demo.RunAsync(settings);
                    break;
            }
            return 0;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 0 or -1 or 2 or 53 or 18456)
        {
            Ui.Error($"Cannot reach SQL Server ({ex.Message}).\nStart it with: docker compose up -d");
            return 1;
        }
    }
}
