using FCPModUpdater.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

public sealed class GitRequiredInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (GitService.IsGitInstalledAsync().GetAwaiter().GetResult()) return;
        
        // It wasn't installed..
        AnsiConsole.MarkupLine("[yellow]This application requires Git to manage mods.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Install Git from: [link]https://git-scm.com/downloads[/]");

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