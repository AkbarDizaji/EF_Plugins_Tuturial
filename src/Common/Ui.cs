using Spectre.Console;

namespace Common;

/// <summary>
/// Console layout used by every demo: PROBLEM → BEFORE → AFTER → RESULT → SQL → WHAT CHANGED.
/// BEFORE is always red, AFTER is always green, so the two are easy to tell apart on video.
/// </summary>
public static class Ui
{
    private const string Line = "====================================================";

    public static void Header(string number, string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold deepskyblue1]{Line}[/]");
        AnsiConsole.MarkupLine($"[bold deepskyblue1]DEMO {number} — {Markup.Escape(title.ToUpperInvariant())}[/]");
        AnsiConsole.MarkupLine($"[bold deepskyblue1]{Line}[/]");
    }

    public static void Problem(string text) => Section("PROBLEM", "yellow", text);

    public static void Before(string text) => Section("BEFORE", "red", text);

    public static void After(string text) => Section("AFTER", "green", text);

    public static void Result(string text) => Section("RESULT", "deepskyblue1", text);

    public static void WhatChanged(string text) => Section("WHAT CHANGED", "mediumpurple1", text);

    private static void Section(string label, string color, string text)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold {color}]{label}:[/]");
        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }

    public static void Info(string text) => AnsiConsole.MarkupLine($"  {Markup.Escape(text)}");

    /// <summary>A short line inside a BEFORE / AFTER section, prefixed in the section's color.</summary>
    public static void Step(bool after, string text) =>
        AnsiConsole.MarkupLine($"  [{(after ? "green" : "red")}]›[/] {Markup.Escape(text)}");

    public static void Good(string text) => AnsiConsole.MarkupLine($"  [green]✔ {Markup.Escape(text)}[/]");

    public static void Bad(string text) => AnsiConsole.MarkupLine($"  [red]✘ {Markup.Escape(text)}[/]");

    public static void Error(string text) => AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(text)}[/]");

    public static void Dim(string text) => AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(text)}[/]");

    /// <summary>Shows SQL only when the demo was started with --sql.</summary>
    public static void Sql(DemoSettings settings, string title, string sql)
    {
        if (!settings.ShowSql) return;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold grey]SQL — {Markup.Escape(title)}:[/]");
        AnsiConsole.Write(new Panel(new Text(sql.Trim(), new Style(Color.Grey70)))
            .Border(BoxBorder.Rounded).BorderColor(Color.Grey35).Expand());
    }

    public static void SqlHint(DemoSettings settings)
    {
        if (!settings.ShowSql)
            Dim("(run with --sql to print the generated SQL)");
    }

    /// <summary>Renders a table. Rows are plain strings, escaped automatically.</summary>
    public static void Table(string? title, string[] columns, IEnumerable<string[]> rows, bool after = true)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(after ? Color.Green : Color.Red);
        if (title is not null) table.Title($"[bold]{Markup.Escape(title)}[/]");
        foreach (var c in columns)
        {
            var col = new TableColumn(Markup.Escape(c));
            table.AddColumn(col);
        }
        foreach (var r in rows) table.AddRow(r.Select(Markup.Escape).ToArray());
        AnsiConsole.Write(table);
    }

    /// <summary>Side-by-side comparison of measured values. Only pass values that were really measured.</summary>
    public static void Compare(string title, params (string Metric, string Before, string After)[] rows)
    {
        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded).Title($"[bold]{Markup.Escape(title)}[/]")
            .AddColumn("Measured")
            .AddColumn(new TableColumn("[red]Before[/]").RightAligned())
            .AddColumn(new TableColumn("[green]After[/]").RightAligned());
        foreach (var (m, b, a) in rows)
            table.AddRow(Markup.Escape(m), $"[red]{Markup.Escape(b)}[/]", $"[green]{Markup.Escape(a)}[/]");
        AnsiConsole.Write(table);
    }

    public static async Task<T> StatusAsync<T>(string text, Func<Task<T>> action) =>
        await AnsiConsole.Status().StartAsync(text, _ => action());

    public static async Task StatusAsync(string text, Func<Task> action) =>
        await AnsiConsole.Status().StartAsync(text, _ => action());
}
