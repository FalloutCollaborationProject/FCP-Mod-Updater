using FCPModUpdater.Gui.Services;
using FCPModUpdater.Gui.ViewModels;

namespace FCPModUpdater.Tests.Gui;

public class VersionBranchGuiTests
{
    private static readonly InstalledMod WrongBranch = new()
    {
        Name = "FCP-Core", Path = "/mods/FCP-Core", Source = ModSource.Git, Branch = "main",
        Status = ModStatus.Behind, CommitsBehind = 2,
        VersionBranch = "rimworld-1.6", VersionMismatch = VersionMismatch.OnMainline
    };

    [Fact]
    public void Row_WrongBranch_IsTaggedAndNotUpdateable()
    {
        var row = new ModRowViewModel(WrongBranch, "1.6");
        Assert.True(row.IsWrongBranch);
        Assert.False(row.CanUpdate);
        Assert.False(row.IsChecked);
        Assert.Equal("Wrong branch for 1.6", row.WrongBranchLabel);
        Assert.Contains("rimworld-1.6", row.WrongBranchTip);

        var fine = new ModRowViewModel(WrongBranch with { VersionMismatch = VersionMismatch.None }, "1.6");
        Assert.False(fine.IsWrongBranch);
        Assert.True(fine.CanUpdate);
    }

    [Fact]
    public void EndScan_AddsSwitchProblem()
    {
        var log = new GitOutputLog(action => action());
        log.BeginScan("/mods");
        log.EndScan([WrongBranch], TimeSpan.FromSeconds(1), "1.6");

        Problem problem = Assert.Single(log.Problems);
        Assert.False(problem.IsError);
        Assert.Equal("rimworld-1.6", problem.SwitchBranch);
        Assert.Equal("Switch to rimworld-1.6", problem.ActionLabel);
    }
}
