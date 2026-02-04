namespace FCPModUpdater.Models;

public record GitCommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Author,
    DateTimeOffset Date
);
