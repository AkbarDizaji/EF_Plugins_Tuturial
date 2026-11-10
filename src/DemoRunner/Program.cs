using Common;
using Spectre.Console;

// dotnet run --project src/DemoRunner -- [menu|all|1|2|3|4|seed|reset] [--fast|--perf] [--sql] [--log-sql]
IDemo[] demos =
[
    new Demo01.Returning.ReturningDemo(),
    new Demo02.QueryComplexity.QueryComplexityDemo(),
    new Demo03.Projectables.ProjectablesDemo(),
    new Demo04.WindowFunctions.WindowFunctionsDemo(),
];

var settings = DemoSettings.Parse(args);
var command = args.FirstOrDefault(a => !a.StartsWith("--"))?.ToLowerInvariant() ?? "menu";

try
{
    switch (command)
    {
        case "all": await RunAsync(demos); break;
        case "seed": await SeedAsync(force: true); break;
        case "reset": await ResetAsync(); break;
        case "menu": await MenuAsync(); break;
        default:
            var demo = demos.FirstOrDefault(d => int.TryParse(command, out var n) && int.Parse(d.Number) == n);
            if (demo is null) { Ui.Error($"Unknown command '{command}'. Use menu, all, 1-4, seed or reset."); return 1; }
            await RunAsync([demo]);
            break;
    }
    return 0;
}
catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 0 or -1 or 2 or 53 or 18456)
{
    Ui.Error($"Cannot reach SQL Server ({ex.Message}).\nStart it with: docker compose up -d");
    return 1;
}

async Task RunAsync(IEnumerable<IDemo> selected)
{
    foreach (var demo in selected)
    {
        await Ui.StatusAsync($"Preparing demo {demo.Number} data ({settings.Size})...", () => demo.SeedAsync(settings));
        await demo.RunAsync(settings);
    }
}

async Task SeedAsync(bool force)
{
    foreach (var demo in demos)
    {
        await Ui.StatusAsync($"Seeding demo {demo.Number} ({settings.Size})...", () => demo.SeedAsync(settings, force));
        Ui.Good($"Demo {demo.Number} — {demo.Title} seeded");
    }
}

async Task ResetAsync()
{
    foreach (var demo in demos)
    {
        await demo.ResetAsync(settings);
        Ui.Good($"Demo {demo.Number} — {demo.Title} database dropped");
    }
}

async Task MenuAsync()
{
    while (true)
    {
        var choices = demos.Select(d => $"{d.Number}  {d.Title}")
            .Concat(["Run all demos", "Seed all databases", "Reset (drop) all databases",
                     $"Toggle SQL output (now {(settings.ShowSql ? "ON" : "OFF")})", "Exit"])
            .ToList();
        AnsiConsole.WriteLine();
        var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title($"[bold]EF Core extensions demo[/] [grey](data size: {settings.Size})[/]")
            .AddChoices(choices));

        if (choice == "Exit") return;
        if (choice == "Run all demos") await RunAsync(demos);
        else if (choice == "Seed all databases") await SeedAsync(force: true);
        else if (choice.StartsWith("Reset")) await ResetAsync();
        else if (choice.StartsWith("Toggle")) settings = settings with { ShowSql = !settings.ShowSql };
        else await RunAsync([demos[choices.IndexOf(choice)]]);
    }
}
