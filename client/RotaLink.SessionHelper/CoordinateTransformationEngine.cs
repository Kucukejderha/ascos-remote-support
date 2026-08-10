using System.Runtime.InteropServices;

namespace RotaLink.SessionHelper;

internal readonly record struct AbsolutePoint(int X, int Y);

internal sealed class CoordinateTransformationEngine
{
    public AbsolutePoint Transform(double x, double y)
    {
        if (!IsNormalized(x) || !IsNormalized(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var width = GetSystemMetrics(78);
        var height = GetSystemMetrics(79);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Invalid virtual desktop metrics.");
        var pixelSpanX = Math.Max(1, width - 1);
        var pixelSpanY = Math.Max(1, height - 1);
        var pixelX = (int)Math.Round(x * pixelSpanX, MidpointRounding.AwayFromZero);
        var pixelY = (int)Math.Round(y * pixelSpanY, MidpointRounding.AwayFromZero);
        return new AbsolutePoint(
            Math.Clamp((int)Math.Round(pixelX * 65535d / pixelSpanX, MidpointRounding.AwayFromZero), 0, 65535),
            Math.Clamp((int)Math.Round(pixelY * 65535d / pixelSpanY, MidpointRounding.AwayFromZero), 0, 65535));
    }

    private static bool IsNormalized(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value is >= 0 and <= 1;
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
