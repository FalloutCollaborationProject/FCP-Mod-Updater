namespace FCPModUpdater.Models;

public record InstalledMod
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required ModSource Source { get; init; }
    public string? RemoteUrl { get; init; }
    public string? Branch { get; init; }
    public GitCommitInfo? CurrentCommit { get; init; }
    public ModStatus Status { get; init; }
    public int CommitsBehind { get; init; }
    public int CommitsAhead { get; init; }
    public string? MatchedRepoName { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasLocalChanges { get; init; }
}
