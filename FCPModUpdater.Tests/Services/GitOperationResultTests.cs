namespace FCPModUpdater.Tests.Services;

public class GitOperationResultTests
{
    [Fact]
    public void FromExitCode_Zero_IsSuccessful()
    {
        GitOperationResult result = GitOperationResult.FromExitCode(0, null);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FromExitCode_OneFourOne_IsSuccessful()
    {
        GitOperationResult result = GitOperationResult.FromExitCode(141, "progress pipe closed");

        Assert.True(result.Success);
        Assert.Equal(141, result.ExitCode);
        Assert.Equal("progress pipe closed", result.Error);
    }

    [Fact]
    public void FromExitCode_NonZero_IsFailure()
    {
        GitOperationResult result = GitOperationResult.FromExitCode(128, "fatal: failed");

        Assert.False(result.Success);
        Assert.Equal(128, result.ExitCode);
        Assert.Equal("fatal: failed", result.Error);
    }
}
