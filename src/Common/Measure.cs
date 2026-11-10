using System.Diagnostics;

namespace Common;

public static class Measure
{
    /// <summary>
    /// Runs <paramref name="action"/> once to warm up (query compilation, connection pool, plan cache),
    /// then <paramref name="iterations"/> more times and returns the median. Approximate, but repeatable.
    /// </summary>
    public static async Task<(T Result, TimeSpan Median)> MedianAsync<T>(Func<Task<T>> action, int iterations = 5)
    {
        var result = await action();
        var times = new List<TimeSpan>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            result = await action();
            times.Add(sw.Elapsed);
        }
        times.Sort();
        return (result, times[times.Count / 2]);
    }

    public static async Task<(T Result, TimeSpan Elapsed)> OnceAsync<T>(Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var result = await action();
        return (result, sw.Elapsed);
    }

    public static string Format(TimeSpan t) =>
        t.TotalMilliseconds < 1000 ? $"{t.TotalMilliseconds:N1} ms" : $"{t.TotalSeconds:N2} s";
}
