namespace FCPModUpdater.Models;

public enum ModStatus
{
    UpToDate,
    Behind,
    Ahead,
    Diverged,
    LocalChanges,
    NonGit,
    Error,
    Unknown
}

/// <summary>How a mod's checkout relates to the branch made for the player's game version.</summary>
public enum VersionMismatch
{
    None,
    /// <summary>On main (or another branch) while a rimworld-X.Y branch exists for this game.</summary>
    OnMainline,
    /// <summary>On the version branch, but HEAD has commits that are not on origin/rimworld-X.Y.</summary>
    AheadOfVersionBranch,
    /// <summary>On a rimworld-X.Y branch for an older game than the player's; the newer build is on main or a newer version branch.</summary>
    OnOlderVersionBranch
}

public enum ModSource
{
    Git,
    Local
}
