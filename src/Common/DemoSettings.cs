namespace Common;

public enum DataSize { Fast, Standard, Performance }

/// <summary>Command-line switches shared by every demo.</summary>
public sealed record DemoSettings(
    DataSize Size = DataSize.Standard,
    bool ShowSql = false,
    bool LogAllSql = false,
    string DatabaseSuffix = "") // tests use "_Tests" so they never touch the databases used for recording
{
    // Port 14333 matches docker-compose.yml. Override with EFDEMO_SQL for another server.
    public static string ServerConnectionString { get; } =
        Environment.GetEnvironmentVariable("EFDEMO_SQL")
        ?? "Server=localhost,14333;User Id=sa;Password=Demo_Passw0rd!;TrustServerCertificate=True;Encrypt=False";

    public string ConnectionStringFor(string database) =>
        $"{ServerConnectionString.TrimEnd(';')};Database={database}{DatabaseSuffix}";

    /// <summary>Picks one of three sizes: fast (quick video takes), standard (spec sizes), performance (large).</summary>
    public int Scale(int fast, int standard, int performance) => Size switch
    {
        DataSize.Fast => fast,
        DataSize.Performance => performance,
        _ => standard
    };

    public static DemoSettings Parse(IEnumerable<string> args)
    {
        var list = args.Select(a => a.ToLowerInvariant()).ToHashSet();
        var size = list.Contains("--perf") ? DataSize.Performance
                 : list.Contains("--fast") ? DataSize.Fast
                 : DataSize.Standard;
        return new DemoSettings(size, list.Contains("--sql"), list.Contains("--log-sql"));
    }
}
