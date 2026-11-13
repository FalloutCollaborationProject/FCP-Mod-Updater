namespace FCPModUpdater.Tests.Services;

public class ModCatalogTests
{
    private static RemoteRepo Repo(string name, params string[] topics) =>
        new(name, $"https://github.com/FalloutCollaborationProject/{name}.git", "main", null, "",
            ["rimworld-mod", .. topics]);

    private static InstalledMod Mod(string name, ModSource source, ModStatus status, string? matched = null) =>
        new() { Name = name, Path = $"/mods/{name}", Source = source, Status = status, MatchedRepoName = matched };

    [Fact]
    public void GetAvailableInstallRepos_ExcludesInstalled_AndPutsNotRecommendedLast()
    {
        IReadOnlyList<RemoteRepo> repos =
        [
            Repo("Zeta"),
            Repo("Alpha", ModCatalog.NotRecommendedTopic),
            Repo("Beta"),
            Repo("Installed")
        ];

        List<RemoteRepo> available = ModCatalog.GetAvailableInstallRepos(repos, ["installed"]);

        Assert.Equal(["Beta", "Zeta", "Alpha"], available.Select(r => r.Name));
    }

    [Fact]
    public void GetUpdateableMods_OnlyGitModsThatAreBehind()
    {
        IReadOnlyList<InstalledMod> mods =
        [
            Mod("Behind", ModSource.Git, ModStatus.Behind),
            Mod("Diverged", ModSource.Git, ModStatus.Diverged),
            Mod("Dirty", ModSource.Git, ModStatus.LocalChanges),
            Mod("Local", ModSource.Local, ModStatus.NonGit, "Local")
        ];

        Assert.Equal(["Behind"], ModCatalog.GetUpdateableMods(mods).Select(m => m.Name));
    }

    [Fact]
    public void GetConvertibleMods_OnlyLocalFoldersWithAMatchingRepo()
    {
        IReadOnlyList<InstalledMod> mods =
        [
            Mod("Git", ModSource.Git, ModStatus.UpToDate),
            Mod("Zip-main", ModSource.Local, ModStatus.NonGit, "Zip"),
            Mod("Unmatched", ModSource.Local, ModStatus.NonGit)
        ];

        Assert.Equal(["Zip-main"], ModCatalog.GetConvertibleMods(mods).Select(m => m.Name));
    }
}

public class ParseBranchInfoTests
{
    [Fact]
    public void ParsesNamesAndDates_AndSkipsTheHeadPointer()
    {
        const string output =
            "origin/HEAD\t2026-11-12T14:03:00+00:00\n" +
            "origin/main\t2026-11-12T14:03:00+00:00\n" +
            "origin/feature/new-textures\t2026-10-01T09:00:00+02:00\n" +
            "origin\t2026-11-12T14:03:00+00:00\n";

        IReadOnlyList<GitBranchInfo> branches = GitService.ParseBranchInfo(output);

        Assert.Equal(["main", "feature/new-textures"], branches.Select(b => b.Name));
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 14, 3, 0, TimeSpan.Zero), branches[0].LastCommitDate);
    }

    [Fact]
    public void MissingDate_IsNull()
    {
        IReadOnlyList<GitBranchInfo> branches = GitService.ParseBranchInfo("origin/dev\n");

        Assert.Equal("dev", Assert.Single(branches).Name);
        Assert.Null(branches[0].LastCommitDate);
    }
}
