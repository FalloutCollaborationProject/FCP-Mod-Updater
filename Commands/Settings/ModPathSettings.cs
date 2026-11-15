using System.ComponentModel;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands.Settings;

[UsedImplicitly]
public class ModPathSettings : CommandSettings
{
    [CommandOption("-d|--directory")]
    [Description("Path to the RimWorld/Mods folder. Overrides the saved folder; auto-discovered if neither is set.")]
    public DirectoryInfo? ModDirectory { get; init; }

    // Read before the command runs (Program.cs) so the HTTP clients get it; declared here for parsing and --help.
    [CommandOption("--github-token <TOKEN>")]
    [Description("GitHub token for this run. Takes priority over GITHUB_TOKEN and the saved token.")]
    public string? GitHubToken { get; init; }

    public override ValidationResult Validate()
    {
        if (ModDirectory != null && !ModDirectory.Exists)
        {
            return ValidationResult.Error($"Directory does not exist: {ModDirectory.FullName}");
        }

        return ValidationResult.Success();
    }
}

[UsedImplicitly]
public class UpdateSettings : ModPathSettings
{
    [CommandOption("--allow-mainline")]
    [Description("Also update mods that are on main although a branch for your RimWorld version exists.")]
    public bool AllowMainline { get; init; }
}
