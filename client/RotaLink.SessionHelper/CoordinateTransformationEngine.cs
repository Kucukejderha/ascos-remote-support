using System.Runtime.InteropServices;

namespace RotaLink.SessionHelper;

internal readonly struct AbsolutePoint
{
    public AbsolutePoint(int x, int y, int pixelX, int pixelY) { X = x; Y = y; PixelX = pixelX; PixelY = pixelY; }
    public int X { get; }
    public int Y { get; }
    public int PixelX { get; }
    public int PixelY { get; }
}

internal sealed class CoordinateTransformationEngine
{
    public AbsolutePoint Transform(double x, double y)
    {
        if (!IsNormalized(x) || !IsNormalized(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Invalid virtual desktop metrics.");
        var pixelSpanX = Math.Max(1, width - 1);
        var pixelSpanY = Math.Max(1, height - 1);
        var pixelX = (int)Math.Round(x * pixelSpanX, MidpointRounding.AwayFromZero);
        var pixelY = (int)Math.Round(y * pixelSpanY, MidpointRounding.AwayFromZero);
        return new AbsolutePoint(
            Clamp((int)Math.Round(pixelX * 65535d / pixelSpanX, MidpointRounding.AwayFromZero)),
            Clamp((int)Math.Round(pixelY * 65535d / pixelSpanY, MidpointRounding.AwayFromZero)),
            left + pixelX,
            top + pixelY);
    }

    private static bool IsNormalized(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 1;
    private static int Clamp(int value) => Math.Max(0, Math.Min(65535, value));
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
