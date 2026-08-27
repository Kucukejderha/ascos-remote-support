using System.Runtime.InteropServices;

namespace RemoteSupport.Protocol;

public readonly struct VirtualDesktopPoint
{
    public VirtualDesktopPoint(int pixelX, int pixelY, int absoluteX, int absoluteY)
    {
        PixelX = pixelX;
        PixelY = pixelY;
        AbsoluteX = absoluteX;
        AbsoluteY = absoluteY;
    }

    public int PixelX { get; }
    public int PixelY { get; }
    public int AbsoluteX { get; }
    public int AbsoluteY { get; }
}

public sealed class CoordinateTransformationEngine
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public VirtualDesktopPoint Transform(double normalizedX, double normalizedY)
    {
        if (double.IsNaN(normalizedX) || double.IsInfinity(normalizedX))
            throw new ArgumentOutOfRangeException(nameof(normalizedX));
        if (double.IsNaN(normalizedY) || double.IsInfinity(normalizedY))
            throw new ArgumentOutOfRangeException(nameof(normalizedY));

        normalizedX = Math.Max(0d, Math.Min(1d, normalizedX));
        normalizedY = Math.Max(0d, Math.Min(1d, normalizedY));

        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Windows returned invalid virtual desktop metrics.");

        // Width - 1 maps the rightmost physical pixel exactly to 65535. The
        // virtual-desktop flag makes 0 address the left/top edge even when the
        // physical origin is negative (a monitor placed left of the primary).
        var xSpan = Math.Max(1, width - 1);
        var ySpan = Math.Max(1, height - 1);
        var relativeX = (int)Math.Round(normalizedX * xSpan, MidpointRounding.AwayFromZero);
        var relativeY = (int)Math.Round(normalizedY * ySpan, MidpointRounding.AwayFromZero);
        var pixelX = checked(left + relativeX);
        var pixelY = checked(top + relativeY);
        var absoluteX = Math.Max(0, Math.Min(65535,
            (int)Math.Round(relativeX * 65535d / xSpan, MidpointRounding.AwayFromZero)));
        var absoluteY = Math.Max(0, Math.Min(65535,
            (int)Math.Round(relativeY * 65535d / ySpan, MidpointRounding.AwayFromZero)));
        return new VirtualDesktopPoint(pixelX, pixelY, absoluteX, absoluteY);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
