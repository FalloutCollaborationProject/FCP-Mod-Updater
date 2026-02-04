using System.ComponentModel;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands.Settings;

[UsedImplicitly]
public class ModPathSettings : CommandSettings
{
    [CommandOption("-d|--directory")]
    [Description("Path to the RimWorld/Mods folder. If not specified, will attempt to auto-discover.")]
    public DirectoryInfo? ModDirectory { get; init; }

    public override ValidationResult Validate()
    {
        if (ModDirectory != null && !ModDirectory.Exists)
        {
            return ValidationResult.Error($"Directory does not exist: {ModDirectory.FullName}");
        }

        return ValidationResult.Success();
    }
}
