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
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
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
        var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = _width, Height = -_height, Planes = 1, BitCount = 32, Compression = 0 } };
        _bitmap = CreateDIBSection(_screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_screenDc == IntPtr.Zero || _memoryDc == IntPtr.Zero || _bitmap == IntPtr.Zero || _bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        _oldBitmap = SelectObject(_memoryDc, _bitmap);
        SetStretchBltMode(_memoryDc, 4);
    }

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
        var sourceWidth = GetSystemMetrics(78); var sourceHeight = GetSystemMetrics(79);
        if (!StretchBlt(_memoryDc, 0, 0, _width, _height, _screenDc, x, y, sourceWidth, sourceHeight, 0x00CC0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var pixels = new byte[_width * _height * 4];
        Marshal.Copy(_bits, pixels, 0, pixels.Length);
        return new CapturedFrame(_width, _height, pixels);
    }

    private void DesktopThreadMain()
    {
        var desktop = OpenDesktop("Default", 0, false, 0x0001u | 0x0080u | 0x0100u);
        try
        {
            if (desktop == IntPtr.Zero || !SetThreadDesktop(desktop))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Screen capture could not attach to the active Windows desktop.");
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
            if (desktop != IntPtr.Zero) CloseDesktop(desktop);
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
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr OpenDesktop(string desktop, uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);
    [DllImport("user32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool StretchBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);

    private sealed class CaptureRequest
    {
        public ManualResetEventSlim Completed { get; } = new(false);
        public CapturedFrame? Frame { get; set; }
        public Exception? Error { get; set; }
    }
}
