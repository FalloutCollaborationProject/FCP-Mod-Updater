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

public enum ModSource
{
    Git,
    Local
}
