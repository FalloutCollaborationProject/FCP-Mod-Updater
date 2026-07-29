using Spectre.Console;

namespace FCPModUpdater.UI;

public static class ProgressReporter
{
    public static async Task<T> WithStatusAsync<T>(
        string status,
        Func<Task<T>> action,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        return await console.Status()
            .Spinner(Spinner.Known.Toggle)
            .SpinnerStyle(TerminalTheme.PrimaryStyle)
            .StartAsync($"SYS> {status.ToUpperInvariant()}", async _ => await action());
    }

    public static async Task WithStatusAsync(
        string status,
        Func<Task> action,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        await console.Status()
            .Spinner(Spinner.Known.Toggle)
            .SpinnerStyle(TerminalTheme.PrimaryStyle)
            .StartAsync($"SYS> {status.ToUpperInvariant()}", async _ => await action());
    }

    public static async Task WithProgressAsync(
        string description,
        IEnumerable<(string Name, Func<ProgressTask, Task> Action)> tasks,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        await console.Progress()
            .Columns(CreateColumns())
            .StartAsync(async ctx =>
            {
                var taskList = tasks.ToList();
                var progressTasks = taskList
                    .Select(t => (Task: ctx.AddTask(t.Name), t.Action))
                    .ToList();

                foreach ((ProgressTask task, var action) in progressTasks)
                {
                    await action(task);
                    task.Value = 100;
                }
            });
    }

    public static async Task<IReadOnlyList<(string Name, bool Success, string? Error)>> WithBatchProgressAsync<T>(
        string description,
        IReadOnlyList<T> items,
        Func<T, string> nameSelector,
        Func<T, IProgress<double>, Task<(bool Success, string? Error)>> action,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var itemTasks = new List<ProgressTask>();

        await console.Progress()
            .Columns(CreateColumns())
            .StartAsync(async ctx =>
            {
                ProgressTask overallTask = ctx.AddTask(
                    $"[bold {TerminalTheme.Phosphor.ToMarkup()}]SYS> {Markup.Escape(description.ToUpperInvariant())}[/]",
                    maxValue: items.Count);

                foreach (T item in items)
                {
                    var name = nameSelector(item);
                    ProgressTask itemTask = ctx.AddTask($"  {Markup.Escape(name)}");
                    itemTasks.Add(itemTask);

                    var progress = new Progress<double>(p => itemTask.Value = p);

                    try
                    {
                        var (success, error) = await action(item, progress);
                        itemTask.Tag(new BatchResult(name, success, error));
                        itemTask.Value = 100;

                        itemTask.Description = success
                            ? $"  [{TerminalTheme.Phosphor.ToMarkup()}]{Markup.Escape(name)}[/]"
                            : $"  [{TerminalTheme.Failure.ToMarkup()}]{Markup.Escape(name)}[/]";
                    }
                    catch (Exception ex)
                    {
                        itemTask.Tag(new BatchResult(name, false, ex.Message));
                        itemTask.Value = 100;
                        itemTask.Description =
                            $"  [{TerminalTheme.Failure.ToMarkup()}]{Markup.Escape(name)}[/]";
                    }

                    overallTask.Increment(1);
                }
            });

        return itemTasks
            .Select(t => (BatchResult)t.Tag!)
            .Select(r => (r.Name, r.Success, r.Error))
            .ToList();
    }

    internal static ProgressColumn[] CreateColumns() =>
    [
        new TaskDescriptionColumn(),
        new ProgressBarColumn
        {
            CompletedStyle = TerminalTheme.PrimaryStyle,
            FinishedStyle = TerminalTheme.BrightStyle,
            RemainingStyle = TerminalTheme.DimStyle,
            IndeterminateStyle = TerminalTheme.WarningStyle
        },
        new PercentageColumn
        {
            Style = TerminalTheme.DimStyle,
            CompletedStyle = TerminalTheme.PrimaryStyle
        },
        new SpinnerColumn(Spinner.Known.Toggle)
        {
            Style = TerminalTheme.PrimaryStyle,
            CompletedText = "OK",
            CompletedStyle = TerminalTheme.PrimaryStyle,
            PendingText = "--",
            PendingStyle = TerminalTheme.DimStyle
        }
    ];

    private record BatchResult(string Name, bool Success, string? Error);
}
