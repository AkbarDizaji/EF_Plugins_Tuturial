using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Common;

/// <summary>
/// Seeding helper: SqlBulkCopy is ~100x faster than SaveChanges for 100k+ rows.
/// Maps every public settable scalar property to a column of the same name and keeps explicit Ids,
/// so the seeded data (and therefore every demo result) is identical on every run.
/// </summary>
public static class BulkInsert
{
    public static async Task WriteAsync<T>(string connectionString, string table, IEnumerable<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && IsScalar(p.PropertyType))
            .ToArray();

        var data = new DataTable();
        foreach (var p in props)
            data.Columns.Add(p.Name, Nullable.GetUnderlyingType(p.PropertyType) ?? (p.PropertyType.IsEnum ? typeof(int) : p.PropertyType));

        foreach (var row in rows)
            data.Rows.Add(props.Select(p => p.GetValue(row) is { } v && p.PropertyType.IsEnum ? (int)v : p.GetValue(row) ?? DBNull.Value).ToArray());

        using var bulk = new SqlBulkCopy(connectionString, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.TableLock)
        {
            DestinationTableName = table,
            BatchSize = 50_000,
            BulkCopyTimeout = 600
        };
        foreach (var p in props) bulk.ColumnMappings.Add(p.Name, p.Name);
        await bulk.WriteToServerAsync(data);
    }

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(string) || t == typeof(DateTime) || t == typeof(Guid);
    }
}
