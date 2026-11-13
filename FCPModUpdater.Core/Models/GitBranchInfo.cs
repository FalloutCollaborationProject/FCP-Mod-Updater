namespace FCPModUpdater.Models;

/// <summary>A remote branch and the date of its tip commit.</summary>
public record GitBranchInfo(string Name, DateTimeOffset? LastCommitDate);
