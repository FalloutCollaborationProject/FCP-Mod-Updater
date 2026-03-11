namespace FCPModUpdater.Tests.Commands;

public class UpdateCommandFilterTests
{
    private static InstalledMod MakeMod(ModSource source, ModStatus status) =>
        new InstalledMod
    {
        Name = "TestMod",
        Path = "/test",
        Source = source,
        Status = status
    };

    [Fact]
    public void BehindAndGit_Included()
    {
        var mods = new[] { MakeMod(ModSource.Git, ModStatus.Behind) };
        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Single(result);
    }

    [Fact]
    public void UpToDateAndGit_Excluded()
    {
        var mods = new[] { MakeMod(ModSource.Git, ModStatus.UpToDate) };
        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Empty(result);
    }

    [Fact]
    public void BehindAndLocal_Excluded()
    {
        var mods = new[] { MakeMod(ModSource.Local, ModStatus.Behind) };
        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Empty(result);
    }

    [Fact]
    public void MixedStatuses_OnlyBehindGitReturned()
    {
        var mods = new[]
        {
            MakeMod(ModSource.Git, ModStatus.Behind),
            MakeMod(ModSource.Git, ModStatus.Ahead),
            MakeMod(ModSource.Git, ModStatus.Diverged),
            MakeMod(ModSource.Git, ModStatus.UpToDate),
            MakeMod(ModSource.Git, ModStatus.Error),
            MakeMod(ModSource.Git, ModStatus.LocalChanges),
            MakeMod(ModSource.Git, ModStatus.NonGit),
            MakeMod(ModSource.Git, ModStatus.Unknown),
        };

        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Single(result);
        Assert.Equal(ModStatus.Behind, result[0].Status);
    }

    [Fact]
    public void EmptyList_ReturnsEmpty()
    {
        var result = UpdateCommand.GetUpdateableMods([]);
        Assert.Empty(result);
    }

    [Fact]
    public void MultipleBehind_AllReturned()
    {
        var mods = new[]
        {
            MakeMod(ModSource.Git, ModStatus.Behind),
            MakeMod(ModSource.Git, ModStatus.Behind),
            MakeMod(ModSource.Git, ModStatus.Behind),
        };

        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void NonGitWithBehindStatus_Excluded()
    {
        var mods = new[] { MakeMod(ModSource.Local, ModStatus.NonGit) };
        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Empty(result);
    }

    [Fact]
    public void ErrorMods_Excluded()
    {
        var mods = new[] { MakeMod(ModSource.Git, ModStatus.Error) };
        var result = UpdateCommand.GetUpdateableMods(mods);
        Assert.Empty(result);
    }
}
