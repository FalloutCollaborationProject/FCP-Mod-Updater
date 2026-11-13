namespace FCPModUpdater.Models;

public record GitCommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Author,
    DateTimeOffset Date
)
{
    public static readonly GitCommitInfo Invalid = new GitCommitInfo("", "", "", "", default);
}
