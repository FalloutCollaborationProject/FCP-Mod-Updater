using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace FCPModUpdater.Tests.UI;

public class TerminalThemeTests
{
    [Fact]
    public void Header_ContainsRobCoIdentityAndApplicationVersion()
    {
        using var console = new TestConsole().Width(100);

        TerminalTheme.WriteHeader(console);

        Assert.Contains("ROBCO INDUSTRIES (TM) TERMLINK", console.Output);
        Assert.Contains("FCP MOD MANAGEMENT SYSTEM", console.Output);
        Assert.Contains(AppVersion.InformationalVersion, console.Output);
        Assert.Contains("SYSTEM ONLINE", console.Output);
    }

    [Fact]
    public void ModTable_UnicodeConsoleUsesTerminalStatusGlyphsAndEscapesNames()
    {
        using var console = new TestConsole().Width(140);
        var mods = new[]
        {
            MakeMod("Current [Core]", ModStatus.UpToDate),
            MakeMod("Update", ModStatus.Behind, behind: 3),
            MakeMod("Ahead", ModStatus.Ahead, ahead: 2),
            MakeMod("Diverged", ModStatus.Diverged, behind: 1, ahead: 4),
            MakeMod("Modified", ModStatus.LocalChanges),
            MakeMod("Failure", ModStatus.Error)
        };

        ModTableRenderer.RenderModTable(mods, console: console);

        Assert.Contains("FCP MOD DATABASE", console.Output);
        Assert.Contains("Current [Core]", console.Output);
        Assert.Contains("✓ CURRENT", console.Output);
        Assert.Contains("↓ 3 BEHIND", console.Output);
        Assert.Contains("↑ 2 AHEAD", console.Output);
        Assert.Contains("⇅ DIVERGED", console.Output);
        Assert.Contains("~ MODIFIED", console.Output);
        Assert.Contains("✗ ERROR", console.Output);
    }

    [Fact]
    public void ModTable_NonUnicodeConsoleUsesAsciiBordersAndGlyphs()
    {
        using var console = new TestConsole()
            .Width(120)
            .SupportsUnicode(false);

        ModTableRenderer.RenderModTable(
            [MakeMod("Broken", ModStatus.Error), MakeMod("Current", ModStatus.UpToDate)],
            console: console);

        Assert.Contains("+", console.Output);
        Assert.Contains("X ERROR", console.Output);
        Assert.Contains("OK CURRENT", console.Output);
        Assert.DoesNotContain("✗", console.Output);
        Assert.DoesNotContain("✓", console.Output);
    }

    [Fact]
    public void PromptFactoriesApplyTerminalStylesAndInstructions()
    {
        var selection = TerminalTheme.Selection<string>("Select operation");
        var multiSelection = TerminalTheme.MultiSelection<string>("Select mods");

        Assert.Equal(TerminalTheme.HighlightStyle, selection.HighlightStyle);
        Assert.Equal(TerminalTheme.HighlightStyle, multiSelection.HighlightStyle);
        Assert.Contains("SELECT OPERATION", selection.Title);
        Assert.Contains("SPACE: MARK/UNMARK", multiSelection.InstructionsText);
        Assert.Contains("ESC: RETURN", multiSelection.InstructionsText);
    }

    [Fact]
    public void AppUpdateNotice_UsesSystemBulletinAndEscapesDynamicValues()
    {
        using var console = new TestConsole().Width(120);
        var result = new UpdateCheckResult(
            "1.0.0",
            "1.1.0[beta]",
            "https://example.test/releases/tag/v1.1.0",
            "Release",
            DateTimeOffset.UtcNow,
            true);

        AppUpdateNoticeRenderer.Render(result, console);

        Assert.Contains("SYSTEM UPDATE BULLETIN", console.Output);
        Assert.Contains("PRE-RELEASE AVAILABLE", console.Output);
        Assert.Contains("1.1.0[beta]", console.Output);
        Assert.Contains("CURRENT: V1.0.0", console.Output);
    }

    [Fact]
    public void ProgressColumnsUseRobCoPaletteAndTerminalMarkers()
    {
        var columns = ProgressReporter.CreateColumns();
        var bar = Assert.IsType<ProgressBarColumn>(columns[1]);
        var percentage = Assert.IsType<PercentageColumn>(columns[2]);
        var spinner = Assert.IsType<SpinnerColumn>(columns[3]);

        Assert.Equal(TerminalTheme.PrimaryStyle, bar.CompletedStyle);
        Assert.Equal(TerminalTheme.DimStyle, bar.RemainingStyle);
        Assert.Equal(TerminalTheme.PrimaryStyle, percentage.CompletedStyle);
        Assert.Equal("OK", spinner.CompletedText);
        Assert.Equal("--", spinner.PendingText);
    }

    private static InstalledMod MakeMod(
        string name,
        ModStatus status,
        int behind = 0,
        int ahead = 0) =>
        new()
        {
            Name = name,
            Path = $"/mods/{name}",
            Source = ModSource.Git,
            Branch = "main",
            Status = status,
            CommitsBehind = behind,
            CommitsAhead = ahead
        };
}
