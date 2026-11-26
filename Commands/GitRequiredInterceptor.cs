using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

public sealed class GitRequiredInterceptor(SettingsStore store) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        // `settings` must stay usable without git, so a git path can be saved from it.
        if (settings is SettingsShowCommand.Options or SettingsSetCommand.Options or SettingsUnsetCommand.Options) return;

        GitService.UseExecutable(store.Current.GitPath);
        if (GitService.IsGitInstalledAsync().GetAwaiter().GetResult()) return;

        // It wasn't installed..
        AnsiConsole.MarkupLine("[yellow]This application requires Git to manage mods.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(store.Current.GitPath is { } saved
            ? $"The saved git path doesn't work: [grey]{Markup.Escape(saved)}[/]"
            : "Git was not found on your PATH.");
        AnsiConsole.MarkupLine("Install Git from: [link]https://git-scm.com/downloads[/]");
        AnsiConsole.MarkupLine("Already installed? Run: [blue]settings set git-path \"<path to git.exe>\"[/]");

        throw new GitNotFoundException();
    }
}

public class GitNotFoundException : Exception
{
    public GitNotFoundException() 
        : base("Git is not installed or not found in PATH.") { }
    
    public GitNotFoundException(string message) 
        : base(message) { }
    
    public GitNotFoundException(string message, Exception inner) 
        : base(message, inner) { }
}