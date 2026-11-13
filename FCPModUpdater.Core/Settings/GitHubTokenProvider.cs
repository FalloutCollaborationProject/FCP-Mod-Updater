namespace FCPModUpdater.Settings;

public enum TokenSource
{
    None,
    Argument,
    Environment,
    Settings,
}

/// <summary>
/// Picks the GitHub token in priority order: <c>--github-token</c>, then <c>GITHUB_TOKEN</c>,
/// then the token saved in settings. Read on every request so a newly saved token applies at once.
/// </summary>
public class GitHubTokenProvider(SettingsStore settings, string? argumentToken = null, Func<string?>? environment = null)
{
    public const string EnvironmentVariable = "GITHUB_TOKEN";

    private readonly Func<string?> _environment = environment ?? (() => System.Environment.GetEnvironmentVariable(EnvironmentVariable));

    public (string? Token, TokenSource Source) Resolve()
    {
        if (!string.IsNullOrWhiteSpace(argumentToken))
            return (argumentToken.Trim(), TokenSource.Argument);

        var env = _environment();
        if (!string.IsNullOrWhiteSpace(env))
            return (env.Trim(), TokenSource.Environment);

        var saved = settings.Current.GitHubToken;
        if (!string.IsNullOrWhiteSpace(saved))
            return (saved.Trim(), TokenSource.Settings);

        return (null, TokenSource.None);
    }

    public string? Token => Resolve().Token;
    public TokenSource ActiveSource => Resolve().Source;
}
