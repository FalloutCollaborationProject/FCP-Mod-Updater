namespace FCPModUpdater.Models;

public sealed record GitOperationResult(
    bool Success,
    int ExitCode,
    string? Error)
{
    public static GitOperationResult FromExitCode(int exitCode, string? error)
    {
        return new GitOperationResult(IsSuccessExitCode(exitCode), exitCode, NormalizeError(error));
    }

    private static bool IsSuccessExitCode(int exitCode)
    {
        // Exit code 141 = SIGPIPE (128+13), harmless when git's progress pipe closes early.
        return exitCode is 0 or 141;
    }

    private static string? NormalizeError(string? error)
    {
        return string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }
}
