using Spectre.Console;

namespace FCPModUpdater.UI;

internal enum TerminalMessageKind
{
    Information,
    Success,
    Warning,
    Failure,
    Muted
}

internal static class TerminalTheme
{
    public static Color Phosphor { get; } = new(0x33, 0xFF, 0x66);
    public static Color Bright { get; } = new(0xB6, 0xFF, 0xC8);
    public static Color Dim { get; } = new(0x16, 0x8A, 0x43);
    public static Color Warning { get; } = new(0xFF, 0xB0, 0x00);
    public static Color Failure { get; } = new(0xFF, 0x5C, 0x57);

    public static Style PrimaryStyle { get; } = new(Phosphor);
    public static Style BrightStyle { get; } = new(Bright, decoration: Decoration.Bold);
    public static Style DimStyle { get; } = new(Dim, decoration: Decoration.Dim);
    public static Style WarningStyle { get; } = new(Warning, decoration: Decoration.Bold);
    public static Style FailureStyle { get; } = new(Failure, decoration: Decoration.Bold);
    public static Style HighlightStyle { get; } = new(Color.Black, Phosphor, Decoration.Bold);

    public static void WriteHeader(IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;

        var content = new Rows(
            new Text("ROBCO INDUSTRIES (TM) TERMLINK", BrightStyle),
            new Text($"FCP MOD MANAGEMENT SYSTEM // {AppVersion.InformationalVersion}", PrimaryStyle));

        var panel = new Panel(content)
        {
            Border = console.Profile.Capabilities.Unicode ? BoxBorder.Heavy : BoxBorder.Ascii,
            BorderStyle = PrimaryStyle,
            Header = new PanelHeader("[[ SYSTEM ONLINE ]]", Justify.Right),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };

        console.Write(panel);
        console.WriteLine();
    }

    public static void WriteMessage(
        string message,
        TerminalMessageKind kind = TerminalMessageKind.Information,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var (prefix, style) = kind switch
        {
            TerminalMessageKind.Success => ("OK>", PrimaryStyle),
            TerminalMessageKind.Warning => ("WARN>", WarningStyle),
            TerminalMessageKind.Failure => ("ERR>", FailureStyle),
            TerminalMessageKind.Muted => ("SYS>", DimStyle),
            _ => ("SYS>", PrimaryStyle)
        };

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(1))
            .AddColumn();
        grid.AddRow(new Text(prefix, style), new Text(message, style));
        console.Write(grid);
    }

    public static void WriteSection(string title, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        console.Write(new Rule(
            $"[bold {Phosphor.ToMarkup()}][[ {Markup.Escape(title.ToUpperInvariant())} ]][/]")
        {
            Border = console.Profile.Capabilities.Unicode ? BoxBorder.Heavy : BoxBorder.Ascii,
            Style = DimStyle,
            Justification = Justify.Left
        });
    }

    public static SelectionPrompt<T> Selection<T>(string title) where T : notnull =>
        new SelectionPrompt<T>()
            .Title($"[bold {Phosphor.ToMarkup()}][[ {Markup.Escape(title.ToUpperInvariant())} ]][/]")
            .HighlightStyle(HighlightStyle)
            .MoreChoicesText($"[{Dim.ToMarkup()}](MOVE UP/DOWN TO VIEW MORE)[/]");

    public static MultiSelectionPrompt<T> MultiSelection<T>(string title) where T : notnull =>
        new MultiSelectionPrompt<T>()
            .Title($"[bold {Phosphor.ToMarkup()}][[ {Markup.Escape(title.ToUpperInvariant())} ]][/]")
            .HighlightStyle(HighlightStyle)
            .InstructionsText(
                $"[{Dim.ToMarkup()}](SPACE: MARK/UNMARK // ENTER: CONFIRM // ESC: RETURN)[/]")
            .MoreChoicesText($"[{Dim.ToMarkup()}](MOVE UP/DOWN TO VIEW MORE)[/]");

    public static TextPrompt<string> TextPrompt(string prompt) =>
        new($"[bold {Phosphor.ToMarkup()}]{Markup.Escape(prompt.ToUpperInvariant())}[/]")
        {
            PromptStyle = PrimaryStyle,
            DefaultValueStyle = BrightStyle,
            ChoicesStyle = DimStyle
        };

    public static ConfirmationPrompt Confirmation(string prompt, bool defaultValue = true) =>
        new($"[bold {Phosphor.ToMarkup()}]{Markup.Escape(prompt.ToUpperInvariant())}[/]")
        {
            DefaultValue = defaultValue,
            DefaultValueStyle = BrightStyle,
            ChoicesStyle = DimStyle
        };

    public static Task<bool> ConfirmAsync(
        string prompt,
        bool defaultValue = true,
        CancellationToken cancellationToken = default,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        return console.PromptAsync(Confirmation(prompt, defaultValue), cancellationToken);
    }

    public static Task<string> AskAsync(
        string prompt,
        CancellationToken cancellationToken = default,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        return console.PromptAsync(TextPrompt(prompt), cancellationToken);
    }

    public static ExceptionSettings ExceptionSettings { get; } = new()
    {
        Format = ExceptionFormats.Default,
        Style = new ExceptionStyle
        {
            Message = FailureStyle,
            Exception = BrightStyle,
            Method = WarningStyle,
            ParameterType = PrimaryStyle,
            ParameterName = BrightStyle,
            Parenthesis = DimStyle,
            Path = WarningStyle,
            LineNumber = PrimaryStyle,
            Dimmed = DimStyle,
            NonEmphasized = PrimaryStyle
        }
    };
}

internal readonly record struct TerminalGlyphs(
    string Success,
    string Behind,
    string Ahead,
    string Diverged,
    string Modified,
    string Empty,
    string Failure)
{
    public static TerminalGlyphs For(IAnsiConsole console) =>
        console.Profile.Capabilities.Unicode
            ? new("✓", "↓", "↑", "⇅", "~", "—", "✗")
            : new("OK", "v", "^", "<>", "~", "-", "X");
}
