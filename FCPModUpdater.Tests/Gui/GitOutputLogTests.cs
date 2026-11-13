using FCPModUpdater.Gui.Services;

namespace FCPModUpdater.Tests.Gui;

public class GitOutputLogTests
{
    private const string Fetch = "fetch --all --prune --progress --recurse-submodules";
    private readonly GitOutputLog _log = new(action => action());

    [Fact]
    public void ProbeCommands_AreNotLogged()
    {
        _log.CommandStarted("/mods/A", "rev-parse --is-inside-work-tree");
        _log.OutputLine("/mods/A", "rev-parse --is-inside-work-tree", "true");
        _log.CommandFinished("/mods/A", "rev-parse --is-inside-work-tree", 128, TimeSpan.Zero, "fatal: not a repo");

        Assert.Empty(_log.Lines);
        Assert.Empty(_log.Problems);
    }

    [Fact]
    public void SuccessfulCommand_LogsCommandOutputAndExit()
    {
        _log.CommandStarted("/mods/FCP-Core", Fetch);
        _log.OutputLine("/mods/FCP-Core", Fetch, "Fetching origin");
        _log.CommandFinished("/mods/FCP-Core", Fetch, 0, TimeSpan.FromSeconds(0.9), null);

        Assert.Equal([LogKind.Command, LogKind.Output, LogKind.Ok], _log.Lines.Select(l => l.Kind));
        Assert.Equal($"$ git {Fetch}", _log.Lines[0].Text);
        Assert.All(_log.Lines, l => Assert.Equal("FCP-Core", l.Mod));
        Assert.Empty(_log.Problems);
    }

    [Fact]
    public void ProgressLines_CollapseIntoOneLinePerPhase_AndReportPercent()
    {
        var percents = new List<double>();
        _log.ProgressReported += (_, percent) => percents.Add(percent);

        _log.CommandStarted("/mods/A", Fetch);
        _log.OutputLine("/mods/A", Fetch, "Receiving objects:  10% (1/10)");
        _log.OutputLine("/mods/A", Fetch, "Receiving objects:  62% (6/10)");
        _log.OutputLine("/mods/A", Fetch, "Resolving deltas:   5% (1/20)");

        Assert.Equal(3, _log.Lines.Count);
        Assert.Equal("Receiving objects:  62% (6/10)", _log.Lines[1].Text);
        Assert.Equal([10, 62, 5], percents);
    }

    [Fact]
    public void FailedFetch_AddsProblem_WhichTheNextScanReplaces()
    {
        _log.BeginScan("/mods");
        _log.CommandStarted("/mods/A", Fetch);
        _log.CommandFinished("/mods/A", Fetch, 128, TimeSpan.FromSeconds(30), "fatal: Could not resolve host");

        Problem problem = Assert.Single(_log.Problems);
        Assert.True(problem.IsError);
        Assert.Equal("Fetch failed", problem.Summary);
        Assert.Equal("fatal: Could not resolve host", problem.Detail);
        Assert.Equal(LogKind.Error, _log.Lines[^1].Kind);

        _log.EndScan([], TimeSpan.Zero);
        _log.BeginScan("/mods");

        Assert.Empty(_log.Problems);
    }

    [Fact]
    public void CancelledCommand_IsNotAProblem()
    {
        _log.CommandStarted("/mods/A", Fetch);
        _log.CommandFinished("/mods/A", Fetch, -1, TimeSpan.Zero, GitService.CancelledError);

        Assert.Empty(_log.Problems);
        Assert.DoesNotContain(_log.Lines, l => l.Kind == LogKind.Error);
    }

    [Fact]
    public void ProblemsFromUserActions_SurviveARescan()
    {
        _log.CommandStarted("/mods/A", "pull --ff-only --progress --recurse-submodules");
        _log.CommandFinished("/mods/A", "pull --ff-only --progress --recurse-submodules", 1, TimeSpan.Zero, "Aborting");

        _log.BeginScan("/mods");
        _log.EndScan([], TimeSpan.Zero);

        Assert.Equal("Pull failed", Assert.Single(_log.Problems).Summary);
    }

    [Fact]
    public void EndScan_WarnsAboutDivergedMods()
    {
        var diverged = new InstalledMod
        {
            Name = "FCP Robots", Path = "/mods/FCP-Robots", Source = ModSource.Git, Branch = "main",
            Status = ModStatus.Diverged, CommitsBehind = 2, CommitsAhead = 1
        };

        _log.BeginScan("/mods");
        _log.EndScan([diverged], TimeSpan.FromSeconds(1));

        Problem problem = Assert.Single(_log.Problems);
        Assert.False(problem.IsError);
        Assert.Contains("2 commits behind and 1 commit ahead", problem.Detail);
        Assert.Contains(_log.Lines, l => l.Kind == LogKind.Warning);
    }
}

public class HumanizeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(120, "2 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(3 * 86400, "3 days ago")]
    [InlineData(21 * 86400, "3 weeks ago")]
    [InlineData(240 * 86400, "8 months ago")]
    [InlineData(800 * 86400, "2 years ago")]
    public void Ago(int secondsAgo, string expected) =>
        Assert.Equal(expected, Humanize.Ago(Now.AddSeconds(-secondsAgo), Now));

    [Theory]
    [InlineData(512, "512 KB")]
    [InlineData(48 * 1024, "48 MB")]
    [InlineData(1536 * 1024, "1.5 GB")]
    public void Size(long kb, string expected) => Assert.Equal(expected, Humanize.Size(kb));
}
