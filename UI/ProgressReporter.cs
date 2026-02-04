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
        Func<T, IProgress<double>, Task<(bool Success, string? Error)>> action)
    {
        var results = new List<(string Name, bool Success, string? Error)>();

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var overallTask = ctx.AddTask($"[bold]{description}[/]", maxValue: items.Count);

                foreach (var item in items)
                {
                    var name = nameSelector(item);
                    var itemTask = ctx.AddTask($"  {name}");

                    var progress = new Progress<double>(p => itemTask.Value = p);

                    try
                    {
                        var (success, error) = await action(item, progress);
                        results.Add((name, success, error));
                        itemTask.Value = 100;

                        if (!success)
                        {
                            itemTask.Description = $"  [red]{name}[/]";
                        }
                        else
                        {
                            itemTask.Description = $"  [green]{name}[/]";
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add((name, false, ex.Message));
                        itemTask.Value = 100;
                        itemTask.Description = $"  [red]{name}[/]";
                    }

                    overallTask.Increment(1);
                }
            });

        return results;
    }
}
