namespace FCPModUpdater.Models;

public record InstalledMod
{
    public static readonly InstalledMod Invalid = new InstalledMod
    {
        Name = "Invalid Mod",
        Path = "",
        Source = default,
    };
    
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

    /// <summary>Id of the <see cref="ModCategory"/> the mod belongs to, when known.</summary>
    public string? CategoryId { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasLocalChanges { get; init; }

    /// <summary>Topics of the matching org repository, empty when none matched.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>The remote rimworld-X.Y branch for the player's game version, when the repo has one.</summary>
    public string? VersionBranch { get; init; }

    public VersionMismatch VersionMismatch { get; init; }

    /// <summary>Set when the fetch during the last scan failed, so the status shown may be stale.</summary>
    public GitOperationResult? FetchError { get; init; }
}
