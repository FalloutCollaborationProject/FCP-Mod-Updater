using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

public sealed class GitRequiredInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (IsGitInstalled()) return;
        
        // It wasn't installed..
        AnsiConsole.MarkupLine("[yellow]This application requires Git to manage mods.[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Install Git from: [link]https://git-scm.com/downloads[/]");

        throw new GitNotFoundException();
    }

    private static bool IsGitInstalled()
    {
        try
        {
            var result = Cli.Wrap("git")
                .WithArguments("--version")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync()
                .GetAwaiter()
                .GetResult();

            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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