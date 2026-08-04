using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RemoteSupport.SessionAgent;

[SupportedOSPlatform("windows")]
public static class NativeConsentPrompt
{
    public static bool Show(string supportCode, Uri operatorUri)
    {
        var message = $"ASCOS Uzaktan Destek bağlantısı hazırlanıyor.\n\nDestek kodu: {supportCode}\nOperatör adresi: {operatorUri}\n\nEkranınızın paylaşılmasına ve uzaktan fare/klavye kontrolüne izin veriyor musunuz?\n\nOturum boyunca konsol penceresi görünür kalacak ve ENTER ile sonlandırılabilecektir.";
        return MessageBox(IntPtr.Zero, message, "ASCOS Uzaktan Destek — Kullanıcı Onayı", 0x00000004u | 0x00000030u | 0x00001000u) == 6;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
