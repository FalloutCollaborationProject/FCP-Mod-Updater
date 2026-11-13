using Avalonia.Media;

namespace FCPModUpdater.Gui.Services;

/// <summary>
/// Brushes view models hand to the view for data-driven colour (status badges, log tones).
/// Values match Styles/Tokens.axaml.
/// </summary>
public static class Palette
{
    public static readonly IBrush Transparent = Brushes.Transparent;

    public static readonly IBrush TextStrong = Brush.Parse("#FCFCFC");
    public static readonly IBrush Text = Brush.Parse("#EFF0F1");
    public static readonly IBrush TextSoft = Brush.Parse("#D1D5D9");
    public static readonly IBrush TextMuted = Brush.Parse("#BDC3C7");
    public static readonly IBrush TextDim = Brush.Parse("#A1A9B1");
    public static readonly IBrush TextFaint = Brush.Parse("#7F8C8D");

    public static readonly IBrush Accent = Brush.Parse("#36E27F");
    public static readonly IBrush AccentHover = Brush.Parse("#5BEA98");
    public static readonly IBrush AccentSurface = Brush.Parse("#1B3326");

    public static readonly IBrush Ok = Brush.Parse("#7FD49A");
    public static readonly IBrush OkSurface = Brush.Parse("#16291D");
    public static readonly IBrush Warn = Brush.Parse("#F6A13B");
    public static readonly IBrush WarnSurface = Brush.Parse("#3A2A16");
    public static readonly IBrush WarnBorder = Brush.Parse("#6B4A1E");
    public static readonly IBrush WarnText = Brush.Parse("#F6D9B5");
    public static readonly IBrush Info = Brush.Parse("#5CC0F0");
    public static readonly IBrush InfoSurface = Brush.Parse("#1A3242");
    public static readonly IBrush Error = Brush.Parse("#ED6A75");
    public static readonly IBrush ErrorSurface = Brush.Parse("#3A1F23");
    public static readonly IBrush ErrorStrong = Brush.Parse("#DA4453");
    public static readonly IBrush ErrorBlock = Brush.Parse("#2A1A1D");
    public static readonly IBrush ErrorBlockBorder = Brush.Parse("#5A2A31");
    public static readonly IBrush ErrorBlockText = Brush.Parse("#F3B5BB");
    public static readonly IBrush WarnBlock = Brush.Parse("#2B2418");
    public static readonly IBrush WarnBlockBorder = Brush.Parse("#5A4220");
    public static readonly IBrush Neutral = Brush.Parse("#A1A9B1");
    public static readonly IBrush NeutralSurface = Brush.Parse("#31363B");
    public static readonly IBrush Track = Brush.Parse("#3A3F44");
    public static readonly IBrush Control = Brush.Parse("#2A2E32");
    public static readonly IBrush Chip = Brush.Parse("#4A5056");
}
