using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RemoteSupport.SessionAgent;

internal sealed class WindowsCompatibility
{
    private const byte Workstation = 1;

    private WindowsCompatibility(string operatingSystem, string installationType, Version version,
        int productType, int frameworkRelease, bool isServer, bool isSupported, string reason)
    {
        OperatingSystem = operatingSystem;
        InstallationType = installationType;
        Version = version;
        ProductType = productType;
        FrameworkRelease = frameworkRelease;
        IsServer = isServer;
        IsSupported = isSupported;
        Reason = reason;
    }

    public string OperatingSystem { get; }
    public string InstallationType { get; }
    public Version Version { get; }
    public int ProductType { get; }
    public int FrameworkRelease { get; }
    public bool IsFramework48OrLater => FrameworkRelease >= 528040;
    public bool IsServer { get; }
    public bool IsSupported { get; }
    public string Reason { get; }

    public string ToDiagnosticString() =>
        "Compatibility: OS=" + OperatingSystem + " " + Version +
        ", ProductType=" + ProductType +
        ", InstallationType=" + InstallationType +
        ", Architecture=" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") +
        ", Process=" + (Environment.Is64BitProcess ? "x64" : "x86") +
        ", NetFrameworkRelease=" + FrameworkRelease +
        ", Supported=" + IsSupported +
        ", Reason=" + Reason + ".";

    public static WindowsCompatibility Evaluate()
    {
        var native = new OsVersionInfo { Size = Marshal.SizeOf(typeof(OsVersionInfo)) };
        var status = RtlGetVersion(ref native);
        if (status != 0)
            return new WindowsCompatibility("Bilinmeyen Windows", "Bilinmiyor", Environment.OSVersion.Version,
                0, ReadFrameworkRelease(), false, false, "RtlGetVersion başarısız: NTSTATUS 0x" + status.ToString("X8"));

        var version = new Version(native.Major, native.Minor, native.Build);
        var isServer = native.ProductType != Workstation;
        var installationType = ReadInstallationType();
        var name = ResolveName(version, isServer);
        var frameworkRelease = ReadFrameworkRelease();

        if (!Environment.Is64BitOperatingSystem)
            return new WindowsCompatibility(name, installationType, version, native.ProductType,
                frameworkRelease, isServer, false, "Üretim desteği x64 işletim sistemi gerektirir");

        if (frameworkRelease < 528040)
            return new WindowsCompatibility(name, installationType, version, native.ProductType,
                frameworkRelease, isServer, false, ".NET Framework 4.8 veya üstü gerekir");

        if (installationType.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
            return new WindowsCompatibility(name, installationType, version, native.ProductType,
                frameworkRelease, isServer, false, "Windows Server Core etkileşimli masaüstü içermez");

        var supportedFamily = isServer
            ? version >= new Version(6, 2)
            : version.Major >= 10;
        if (!supportedFamily)
            return new WindowsCompatibility(name, installationType, version, native.ProductType,
                frameworkRelease, isServer, false, "Desteklenen Windows ailesinin dışında");

        return new WindowsCompatibility(name, installationType, version, native.ProductType,
            frameworkRelease, isServer, true,
            isServer && version < new Version(10, 0)
                ? "Eski sunucu uyumluluk hattı; Desktop Experience, .NET 4.8 ve güncel ESU gerekir"
                : "Uyumluluk adayı");
    }

    internal static string ResolveName(Version version, bool isServer)
    {
        if (!isServer) return version.Major >= 10 && version.Build >= 22000 ? "Windows 11" :
            version.Major >= 10 ? "Windows 10" : "Destek dışı Windows istemcisi";
        if (version.Major == 6 && version.Minor == 2) return "Windows Server 2012";
        if (version.Major == 6 && version.Minor == 3) return "Windows Server 2012 R2";
        if (version.Major == 10 && version.Build <= 14393) return "Windows Server 2016";
        if (version.Major == 10 && version.Build <= 17763) return "Windows Server 2019";
        if (version.Major == 10 && version.Build <= 20348) return "Windows Server 2022";
        if (version.Major == 10) return "Windows Server 2025 veya üstü";
        return "Bilinmeyen Windows Server";
    }

    private static int ReadFrameworkRelease()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return key?.GetValue("Release") is int release ? release : 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    private static string ReadInstallationType()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("InstallationType") as string ?? "Bilinmiyor";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "Okunamadı";
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfo
    {
        public int Size;
        public int Major;
        public int Minor;
        public int Build;
        public int PlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref OsVersionInfo versionInformation);
}
