using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RemoteSupport.SessionAgent;

public sealed class CapturedFrame
{
    public CapturedFrame(int width, int height, byte[] pixels) { Width = width; Height = height; Pixels = pixels; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
}

public sealed class GdiScreenCapture : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private IntPtr _screenDc;
    private IntPtr _memoryDc;
    private IntPtr _sourceDc;
    private IntPtr _sourceBitmap;
    private IntPtr _oldSourceBitmap;
    private int _sourceWidth;
    private int _sourceHeight;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private byte[]? _pixelBuffer;
    private readonly BlockingCollection<CaptureRequest> _requests = new();
    private readonly ManualResetEventSlim _initialized = new(false);
    private readonly Thread _desktopThread;
    private Exception? _initializationError;

    public GdiScreenCapture(int width, int height)
    {
        _width = width; _height = height;
        _desktopThread = new Thread(DesktopThreadMain) { IsBackground = true, Name = "RotaLink capture desktop" };
        _desktopThread.Start();
        _initialized.Wait();
        if (_initializationError is not null) throw _initializationError;
    }

    private void InitializeNativeCapture()
    {
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        _sourceDc = CreateCompatibleDC(_screenDc);
        _sourceWidth = GetSystemMetrics(78);
        _sourceHeight = GetSystemMetrics(79);
        _sourceBitmap = CreateCompatibleBitmap(_screenDc, _sourceWidth, _sourceHeight);
        var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = _width, Height = -_height, Planes = 1, BitCount = 32, Compression = 0 } };
        _bitmap = CreateDIBSection(_screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_screenDc == IntPtr.Zero || _memoryDc == IntPtr.Zero || _sourceDc == IntPtr.Zero ||
            _sourceBitmap == IntPtr.Zero || _bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _oldSourceBitmap = SelectObject(_sourceDc, _sourceBitmap);
        _oldBitmap = SelectObject(_memoryDc, _bitmap);
        SetStretchBltMode(_memoryDc, 4);
        if (!_sourceLogged)
        {
            _sourceLogged = true;
            AppDiagnostics.Write("GDI capture source: virtual origin=" + GetSystemMetrics(76) + "," + GetSystemMetrics(77) +
                ", size=" + _sourceWidth + "x" + _sourceHeight + ".");
        }
    }

    private static bool _sourceLogged;

    public CapturedFrame Capture()
    {
        var request = new CaptureRequest();
        _requests.Add(request);
        request.Completed.Wait();
        if (request.Error is not null) throw request.Error;
        return request.Frame!;
    }

    private CapturedFrame CaptureOnDesktop()
    {
        var x = GetSystemMetrics(76); var y = GetSystemMetrics(77);
        const uint sourceCopyWithLayeredWindows = 0x40CC0020;
        if (!BitBlt(_sourceDc, 0, 0, _sourceWidth, _sourceHeight, _screenDc, x, y, sourceCopyWithLayeredWindows))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!StretchBlt(_memoryDc, 0, 0, _width, _height, _sourceDc, 0, 0, _sourceWidth, _sourceHeight, 0x00CC0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _pixelBuffer ??= new byte[_width * _height * 4];
        Marshal.Copy(_bits, _pixelBuffer, 0, _pixelBuffer.Length);
        return new CapturedFrame(_width, _height, _pixelBuffer);
    }

    private void DesktopThreadMain()
    {
        try
        {
            InitializeNativeCapture();
            _initialized.Set();
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                try { request.Frame = CaptureOnDesktop(); }
                catch (Exception ex) { request.Error = ex; }
                finally { request.Completed.Set(); }
            }
        }
        catch (Exception ex)
        {
            _initializationError = ex;
            _initialized.Set();
        }
        finally
        {
            CleanupNativeCapture();
        }
    }

    public void Dispose()
    {
        _requests.CompleteAdding();
        _desktopThread.Join(TimeSpan.FromSeconds(2));
        _requests.Dispose();
        _initialized.Dispose();
    }

    private void CleanupNativeCapture()
    {
        if (_oldSourceBitmap != IntPtr.Zero) SelectObject(_sourceDc, _oldSourceBitmap);
        if (_oldBitmap != IntPtr.Zero) SelectObject(_memoryDc, _oldBitmap);
        if (_sourceBitmap != IntPtr.Zero) DeleteObject(_sourceBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
        if (_sourceDc != IntPtr.Zero) DeleteDC(_sourceDc);
        if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
        if (_screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screenDc);
        _screenDc = _memoryDc = _sourceDc = _bitmap = _sourceBitmap = _oldBitmap = _oldSourceBitmap = _bits = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public uint ColorsUsed; public uint ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool StretchBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
    [DllImport("gdi32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);

    private sealed class CaptureRequest
    {
        public ManualResetEventSlim Completed { get; } = new(false);
        public CapturedFrame? Frame { get; set; }
        public Exception? Error { get; set; }
    }
}
