namespace FCPModUpdater.Services;

/// <summary>Runs a batch of independent operations with a bounded number in flight at once.</summary>
public static class ParallelRunner
{
    /// <summary>Batches smaller than this always run one item at a time.</summary>
    public const int MinItemsForParallel = 3;

    public const int MaxParallelism = 8;

    /// <summary>How many items of a batch of <paramref name="itemCount"/> run at once for a configured limit.</summary>
    public static int ResolveParallelism(int itemCount, int configured)
    {
        if (itemCount < MinItemsForParallel)
            return 1;

        return Math.Clamp(configured, 1, Math.Min(itemCount, MaxParallelism));
    }

    /// <summary>
    /// Runs <paramref name="action"/> for every item, at most <paramref name="maxParallelism"/> at a time.
    /// Results come back in input order. Items start in order and stay on the caller's synchronization
    /// context (the UI thread in the GUI), so the action may touch UI state between awaits.
    /// The action should handle its own failures; an exception from it is rethrown after all items finish,
    /// and cancelling <paramref name="ct"/> stops items that have not started yet.
    /// </summary>
    public static async Task<IReadOnlyList<TResult>> RunAsync<T, TResult>(
        IReadOnlyList<T> items,
        int maxParallelism,
        Func<T, int, CancellationToken, Task<TResult>> action,
        CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, maxParallelism));

        async Task<TResult> RunOne(int i)
        {
            await gate.WaitAsync(ct);
            try
            {
                // WaitAsync can still hand a freed slot to a waiter after ct is cancelled
                // (its cancellation cleanup runs asynchronously), so check again once inside.
                ct.ThrowIfCancellationRequested();
                return await action(items[i], i, ct);
            }
            finally
            {
                gate.Release();
            }
        }

        var tasks = new Task<TResult>[items.Count];
        for (var i = 0; i < items.Count; i++)
            tasks[i] = RunOne(i);

        return await Task.WhenAll(tasks);
    }
}
