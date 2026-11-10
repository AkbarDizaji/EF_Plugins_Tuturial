using Common;

namespace EfCoreExtensionsDemo.Tests;

/// <summary>Small datasets in separate "_Tests" databases on the docker-compose SQL Server.</summary>
public static class TestSettings
{
    public static readonly DemoSettings Fast = new(DataSize.Fast, DatabaseSuffix: "_Tests");
}
