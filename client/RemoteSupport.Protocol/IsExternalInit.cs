#if NET48
namespace System.Runtime.CompilerServices
{
    // The compiler emits init-only setters for C# 9+ records, which require
    // this type on .NET Framework runtimes that predate it.
    internal static class IsExternalInit { }
}

namespace System.Runtime.Versioning
{
    // net5+ attribute; the client projects only ever target Windows.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { }
    }
}
#endif
