using Avalonia;
using Avalonia.Controls;

namespace FCPModUpdater.Gui.Controls;

/// <summary>
/// Lays children out in as many equal-width columns as fit (CSS <c>repeat(auto-fill, minmax(min, 1fr))</c>).
/// </summary>
public class AutoGrid : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<AutoGrid, double>(nameof(MinColumnWidth), 320);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<AutoGrid, double>(nameof(Gap), 12);

    static AutoGrid()
    {
        AffectsMeasure<AutoGrid>(MinColumnWidthProperty, GapProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private (int Columns, double Width) Columns(double available)
    {
        if (double.IsInfinity(available))
            return (Math.Max(1, Children.Count), MinColumnWidth);

        var columns = Math.Max(1, (int)Math.Floor((available + Gap) / (MinColumnWidth + Gap)));
        // Whole pixels, so layout rounding cannot push the last column past the edge.
        var width = Math.Max(0, Math.Floor((available - Gap * (columns - 1)) / columns));
        return (columns, width);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Columns(availableSize.Width);
        double total = 0, rowHeight = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(width, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);

            if (i % columns == columns - 1 || i == Children.Count - 1)
            {
                total += rowHeight + (total > 0 ? Gap : 0);
                rowHeight = 0;
            }
        }

        var usedWidth = double.IsInfinity(availableSize.Width)
            ? columns * width + Gap * (columns - 1)
            : availableSize.Width;
        return new Size(usedWidth, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Columns(finalSize.Width);
        double y = 0;

        for (var start = 0; start < Children.Count; start += columns)
        {
            var end = Math.Min(start + columns, Children.Count);
            double rowHeight = 0;
            for (var i = start; i < end; i++)
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);

            for (var i = start; i < end; i++)
                Children[i].Arrange(new Rect((i - start) * (width + Gap), y, width, rowHeight));

            y += rowHeight + Gap;
        }

        return finalSize;
    }
}
