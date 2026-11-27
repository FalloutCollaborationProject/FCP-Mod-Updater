namespace FCPModUpdater.Tests.Services;

public class ParallelRunnerTests
{
    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(2, 4, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(10, 4, 4)]
    [InlineData(10, 0, 1)]
    public void ResolveParallelism_OnlyParallelForTwoOrMore(int count, int configured, int expected)
    {
        Assert.Equal(expected, ParallelRunner.ResolveParallelism(count, configured));
    }

    [Fact]
    public void ResolveParallelism_CappedAtMaxParallelism()
    {
        Assert.Equal(ParallelRunner.MaxParallelism, ParallelRunner.ResolveParallelism(100, 100));
        Assert.InRange(ParallelRunner.MaxParallelism, 1, 16);
    }

    [Fact]
    public async Task RunAsync_NeverExceedsLimit_AndKeepsInputOrder()
    {
        var running = 0;
        var peak = 0;
        var gate = new object();
        int[] items = Enumerable.Range(0, 12).ToArray();

        var results = await ParallelRunner.RunAsync(items, 3, async (item, index, ct) =>
        {
            lock (gate) peak = Math.Max(peak, ++running);
            await Task.Delay(10 + (12 - item) * 2, ct);
            lock (gate) running--;
            return item * 10 + index;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(3, peak);
        Assert.Equal(items.Select(i => i * 11), results);
    }

    [Fact]
    public async Task RunAsync_ParallelismOne_RunsSequentially()
    {
        var order = new List<string>();

        await ParallelRunner.RunAsync(["a", "b", "c"], 1, async (item, _, ct) =>
        {
            order.Add($"start {item}");
            await Task.Delay(5, ct);
            order.Add($"end {item}");
            return 0;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["start a", "end a", "start b", "end b", "start c", "end c"], order);
    }

    [Fact]
    public async Task RunAsync_OneThrowingItem_DoesNotStopOthers_AndIsRethrown()
    {
        var finished = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ParallelRunner.RunAsync([1, 2, 3, 4], 2,
            async (item, _, ct) =>
            {
                await Task.Yield();
                if (item == 1)
                    throw new InvalidOperationException("boom");
                Interlocked.Increment(ref finished);
                return item;
            }, TestContext.Current.CancellationToken));

        Assert.Equal(3, finished);
    }

    [Fact]
    public async Task RunAsync_Cancelled_DoesNotStartQueuedItems()
    {
        using var cts = new CancellationTokenSource();
        var started = 0;
        var release = new TaskCompletionSource();

        Task run = ParallelRunner.RunAsync([1, 2, 3, 4], 2, async (_, _, _) =>
        {
            Interlocked.Increment(ref started);
            await release.Task;
            return 0;
        }, cts.Token);

        cts.Cancel();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, started);
    }
}
