namespace Common;

public interface IDemo
{
    string Number { get; }
    string Title { get; }

    /// <summary>Creates the demo database (if needed) and loads deterministic data.</summary>
    Task SeedAsync(DemoSettings settings, bool force = false);

    Task ResetAsync(DemoSettings settings);

    Task RunAsync(DemoSettings settings);
}
