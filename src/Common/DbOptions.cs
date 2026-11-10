using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Common;

public static class DbOptions
{
    /// <summary>
    /// Plain SQL Server options shared by every demo. Each demo adds its own extension on top,
    /// so the only difference between "before" and "after" is the extension itself.
    /// </summary>
    public static DbContextOptionsBuilder UseDemoSqlServer(
        this DbContextOptionsBuilder builder,
        DemoSettings settings,
        string database,
        SqlCapture? capture = null,
        Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
    {
        builder.UseSqlServer(settings.ConnectionStringFor(database), o =>
        {
            o.CommandTimeout(300);
            sqlServer?.Invoke(o);
        });

        if (capture is not null)
            builder.AddInterceptors(capture);

        if (settings.LogAllSql)
            builder.LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information);

        return builder;
    }
}
