using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RemoteSupport.SessionAgent;

public sealed record CapturedFrame(int Width, int Height, byte[] Pixels);

public sealed class GdiScreenCapture : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly IntPtr _screenDc;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _oldBitmap;
    private readonly IntPtr _bits;

    public GdiScreenCapture(int width, int height)
    {
        _width = width; _height = height;
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32, Compression = 0 } };
        _bitmap = CreateDIBSection(_screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_screenDc == IntPtr.Zero || _memoryDc == IntPtr.Zero || _bitmap == IntPtr.Zero || _bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        _oldBitmap = SelectObject(_memoryDc, _bitmap);
        SetStretchBltMode(_memoryDc, 4);
    }

    public CapturedFrame Capture()
    {
        var x = GetSystemMetrics(76); var y = GetSystemMetrics(77);
        var sourceWidth = GetSystemMetrics(78); var sourceHeight = GetSystemMetrics(79);
        if (!StretchBlt(_memoryDc, 0, 0, _width, _height, _screenDc, x, y, sourceWidth, sourceHeight, 0x00CC0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var pixels = new byte[_width * _height * 4];
        Marshal.Copy(_bits, pixels, 0, pixels.Length);
        return new(_width, _height, pixels);
    }

    public void Dispose()
    {
        if (_oldBitmap != IntPtr.Zero) SelectObject(_memoryDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        if (_screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screenDc);
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public uint ColorsUsed; public uint ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool StretchBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
}
