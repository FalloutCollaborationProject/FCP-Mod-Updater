using FCPModUpdater.Commands;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Spectre.Console;

namespace FCPModUpdater.UI;

/// <summary>The interactive menu's Settings screen.</summary>
public class SettingsMenu(SettingsStore store, GitHubTokenProvider tokens, IGitHubApiService gitHubApi)
{
    private const string Folder = "Mods folder";
    private const string Token = "GitHub token";
    private const string TestToken = "Test GitHub token";
    private const string RemoveToken = "Remove saved token";
    private const string CheckUpdates = "Check for updates on launch";
    private const string Prereleases = "Include pre-releases";
    private const string Back = "Back";

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold]Settings[/]").LeftJustified());
            SettingsShowCommand.Render(store, tokens);
            AnsiConsole.WriteLine();

            AppSettings s = store.Current;
            var choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("[bold]Change a setting[/]")
                    .AddCancelResult(Back)
                    .AddChoices(Folder, Token, TestToken, RemoveToken,
                        $"{CheckUpdates}: {OnOff(s.CheckForUpdatesOnLaunch)}",
                        $"{Prereleases}: {OnOff(s.EffectiveIncludePrereleases)}",
                        Back), ct);

            if (choice == Back)
                return;

            if (choice == Folder)
            {
                var path = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>("Mods folder (empty to auto-detect):").AllowEmpty(), ct);
                Apply(SettingsEditor.ModsDirectory, string.IsNullOrWhiteSpace(path) ? null : path);
                AnsiConsole.MarkupLine("[grey]Takes effect next time the app starts.[/]");
                Pause();
            }
            else if (choice == Token)
            {
                var token = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>("Personal access token:").Secret().AllowEmpty(), ct);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    Apply(SettingsEditor.GitHubToken, token);
                    await TestAsync(token, ct);
                }
                Pause();
            }
            else if (choice == TestToken)
            {
                await TestAsync(null, ct);
                Pause();
            }
            else if (choice == RemoveToken)
            {
                Apply(SettingsEditor.GitHubToken, null);
                Pause();
            }
            else if (choice.StartsWith(CheckUpdates))
            {
                store.Update(x => x with { CheckForUpdatesOnLaunch = !x.CheckForUpdatesOnLaunch });
            }
            else if (choice.StartsWith(Prereleases))
            {
                store.Update(x => x with { IncludePrereleases = !x.EffectiveIncludePrereleases });
            }
        }
    }

    private async Task TestAsync(string? token, CancellationToken ct)
    {
        RateLimitCheck check = await ProgressReporter.WithStatusAsync("Asking GitHub...",
            () => gitHubApi.GetRateLimitAsync(token, ct));
        AnsiConsole.MarkupLine(check.Info is { } info
            ? $"[green]✓ {(token is not null || tokens.ActiveSource != TokenSource.None ? "Token valid" : "No token in use")} · {info.Remaining:N0} of {info.Limit:N0} requests left · resets {info.Reset.ToLocalTime():HH:mm}[/]"
            : $"[red]✗ {Markup.Escape(check.Error ?? "Unknown error")}[/]");
    }

    private void Apply(string key, string? value)
    {
        var (result, error) = SettingsEditor.Apply(store.Current, key, value);
        if (result is null)
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error!)}[/]");
        else
            store.Save(result);
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static void Pause()
    {
        AnsiConsole.MarkupLine("\n[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
