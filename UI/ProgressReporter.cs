using Spectre.Console;

namespace FCPModUpdater.UI;

public static class ProgressReporter
{
    public static async Task<T> WithStatusAsync<T>(string status, Func<Task<T>> action)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync(status, async _ => await action());
    }

    public static async Task WithStatusAsync(string status, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync(status, async _ => await action());
    }

    public static async Task WithProgressAsync(
        string description,
        IEnumerable<(string Name, Func<ProgressTask, Task> Action)> tasks)
    {
        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
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
        int maxParallelism = 1)
    {
        IReadOnlyList<BatchResult> results = [];

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                ProgressTask overallTask = ctx.AddTask($"[bold]{description}[/]", maxValue: items.Count);

                // Rows are created up front so their order stays stable while items finish out of order.
                var rows = items
                    .Select(item => (Name: nameSelector(item), Task: ctx.AddTask($"  {Markup.Escape(nameSelector(item))}")))
                    .ToList();

                results = await FCPModUpdater.Services.ParallelRunner.RunAsync(items, maxParallelism,
                    async (item, i, _) =>
                    {
                        var (name, itemTask) = rows[i];
                        var progress = new DirectProgress(p => itemTask.Value = Math.Max(itemTask.Value, p));

                        BatchResult result;
                        try
                        {
                            var (success, error) = await action(item, progress);
                            result = new BatchResult(name, success, error);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            result = new BatchResult(name, false, ex.Message);
                        }

                        itemTask.Value = 100;
                        itemTask.Description = result.Success
                            ? $"  [green]{Markup.Escape(name)}[/]"
                            : $"  [red]{Markup.Escape(name)}[/]";
                        overallTask.Increment(1);
                        return result;
                    });
            });

        return results.Select(r => (r.Name, r.Success, r.Error)).ToList();
    }

    /// <summary>Applies values on the reporting thread; Progress&lt;T&gt; would post them after the item finished.</summary>
    private sealed class DirectProgress(Action<double> apply) : IProgress<double>
    {
        public void Report(double value) => apply(value);
    }

    private record BatchResult(string Name, bool Success, string? Error);
}
