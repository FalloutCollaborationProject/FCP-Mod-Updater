namespace FCPModUpdater.Services;

/// <summary>
/// Receives a live transcript of the git commands <see cref="GitService"/> runs.
/// Implementations must be thread-safe: commands for different mods run in parallel.
/// </summary>
public interface IGitOutputSink
{
    /// <param name="target">Directory the command is about (the mod folder; for clone, the folder being created).</param>
    void CommandStarted(string target, string arguments);

    void OutputLine(string target, string arguments, string line);

    void CommandFinished(string target, string arguments, int exitCode, TimeSpan duration, string? error);
}
