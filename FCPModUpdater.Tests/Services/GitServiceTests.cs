namespace FCPModUpdater.Tests.Services;

public class ParseCommitLogTests
{
    [Fact]
    public void SingleCommit_ParsedCorrectly()
    {
        var input = "abc123full\nabc123\nFix bug\nJohn\n2024-01-15T10:30:00+00:00\n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Single(result);
        Assert.Equal("abc123full", result[0].Hash);
        Assert.Equal("abc123", result[0].ShortHash);
        Assert.Equal("Fix bug", result[0].Message);
        Assert.Equal("John", result[0].Author);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero), result[0].Date);
    }

    [Fact]
    public void MultipleCommits_AllParsed()
    {
        var input = "hash1\nshort1\nMsg1\nAuthor1\n2024-01-15T10:30:00+00:00\n---\nhash2\nshort2\nMsg2\nAuthor2\n2024-02-20T12:00:00+00:00\n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("hash1", result[0].Hash);
        Assert.Equal("hash2", result[1].Hash);
    }

    [Fact]
    public void EmptyString_ReturnsEmpty()
    {
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog("");
        Assert.Empty(result);
    }

    [Fact]
    public void IncompleteEntry_Skipped()
    {
        var input = "abc123\nabc1234\n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);
        Assert.Empty(result);
    }

    [Fact]
    public void TrailingWhitespace_Trimmed()
    {
        var input = "  abc123  \n  abc1  \n  Fix bug  \n  John  \n  2024-01-15T10:30:00+00:00  \n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Single(result);
        Assert.Equal("abc123", result[0].Hash);
        Assert.Equal("abc1", result[0].ShortHash);
        Assert.Equal("Fix bug", result[0].Message);
        Assert.Equal("John", result[0].Author);
    }

    [Fact]
    public void InvalidDate_ReturnsMinValue()
    {
        var input = "abc123\nabc1\nFix bug\nJohn\nnot-a-date\n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Single(result);
        Assert.Equal(DateTimeOffset.MinValue, result[0].Date);
    }

    [Fact]
    public void MixedValidAndInvalid_OnlyValidReturned()
    {
        var input = "hash1\nshort1\nMsg1\nAuthor1\n2024-01-15T10:30:00+00:00\n---\nincomplete\nentry\n---";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Single(result);
        Assert.Equal("hash1", result[0].Hash);
    }

    [Fact]
    public void EntryWithNoTrailingSeparator_StillParsed()
    {
        var input = "abc123\nabc1\nFix bug\nJohn\n2024-01-15T10:30:00+00:00";
        IReadOnlyList<GitCommitInfo> result = GitService.ParseCommitLog(input);

        Assert.Single(result);
        Assert.Equal("abc123", result[0].Hash);
    }
}
