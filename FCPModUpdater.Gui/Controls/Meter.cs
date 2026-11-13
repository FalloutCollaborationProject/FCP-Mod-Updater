using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FCPModUpdater.Gui.Controls;

/// <summary>
/// A flat rounded progress track; <see cref="Value"/> runs from 0 to 100.
/// </summary>
public class Meter : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Meter, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Meter, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<Meter, IBrush?>(nameof(Track));

    static Meter()
    {
        AffectsRender<Meter>(ValueProperty, FillProperty, TrackProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var radius = Bounds.Height / 2;
        var track = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, track, radius, radius);

        var fraction = Math.Clamp(Value, 0, 100) / 100;
        if (fraction <= 0)
            return;

        using (context.PushClip(new RoundedRect(track, radius)))
        {
            context.DrawRectangle(Fill, null, new Rect(0, 0, Bounds.Width * fraction, Bounds.Height));
        }
    }
}
