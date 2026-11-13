using System.ComponentModel;
using FCPModUpdater.Settings;
using FCPModUpdater.UI;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

[UsedImplicitly]
public class SettingsShowCommand(SettingsStore store, GitHubTokenProvider tokens) : Command<SettingsShowCommand.Options>
{
    [UsedImplicitly]
    public class Options : CommandSettings;

    protected override int Execute(CommandContext context, Options settings, CancellationToken ct)
    {
        Render(store, tokens);
        return 0;
    }

    public static void Render(SettingsStore store, GitHubTokenProvider tokens)
    {
        AppSettings s = store.Current;
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Setting").AddColumn("Value");
        table.AddRow(SettingsEditor.ModsDirectory, Markup.Escape(s.ModsDirectory ?? "(auto-detect)"));
        table.AddRow(SettingsEditor.GitHubToken, Markup.Escape(SettingsEditor.MaskToken(s.GitHubToken)));
        table.AddRow(SettingsEditor.CheckForUpdates, s.CheckForUpdatesOnLaunch.ToString().ToLowerInvariant());
        table.AddRow(SettingsEditor.IncludePrereleases,
            s.IncludePrereleases is { } pre
                ? pre.ToString().ToLowerInvariant()
                : $"{s.EffectiveIncludePrereleases.ToString().ToLowerInvariant()} [grey](default)[/]");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Token in use:[/] {Markup.Escape(SettingsEditor.DescribeSource(tokens.ActiveSource))}");
        if (s.LastUpdateCheck is { } last)
            AnsiConsole.MarkupLine($"[grey]Last update check:[/] {last:g} · {Markup.Escape(s.LastUpdateCheckResult is { } v ? $"v{v} available" : "up to date")}");
        AnsiConsole.MarkupLine($"[grey]File:[/] {Markup.Escape(store.FilePath)}");
    }
}

[UsedImplicitly]
public class SettingsSetCommand(SettingsStore store) : Command<SettingsSetCommand.Options>
{
    [UsedImplicitly]
    public class Options : CommandSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("mods-directory, github-token, check-updates or include-prereleases")]
        public required string Key { get; init; }

        [CommandArgument(1, "<VALUE>")]
        public required string Value { get; init; }
    }

    protected override int Execute(CommandContext context, Options options, CancellationToken ct)
        => SettingsCommandHelpers.Apply(store, options.Key, options.Value);
}

[UsedImplicitly]
public class SettingsUnsetCommand(SettingsStore store) : Command<SettingsUnsetCommand.Options>
{
    [UsedImplicitly]
    public class Options : CommandSettings
    {
        [CommandArgument(0, "<KEY>")]
        [Description("The setting to reset to its default")]
        public required string Key { get; init; }
    }

    protected override int Execute(CommandContext context, Options options, CancellationToken ct)
        => SettingsCommandHelpers.Apply(store, options.Key, null);
}

internal static class SettingsCommandHelpers
{
    public static int Apply(SettingsStore store, string key, string? value)
    {
        var (result, error) = SettingsEditor.Apply(store.Current, key, value);
        if (result is null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error!)}[/]");
            return 1;
        }

        store.Save(result);
        AnsiConsole.MarkupLine(value is null
            ? $"[green]Reset {Markup.Escape(key)}[/]"
            : $"[green]Saved {Markup.Escape(key)}[/]");
        return 0;
    }
}
