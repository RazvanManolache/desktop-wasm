using System.Text;

namespace System.Windows.Forms;

// Headless encoding boot: the browser runtime ships only a small set of
// text encodings, and subtitle-style apps query legacy codepages
// (SubtitleEdit: 28606 = ISO-8859-16) which throw NotSupportedException
// and cascade into NRE/IndexOutOfRange follow-ons. Registering the
// inbox CodePages provider once makes the 28591-28605 + CJK
// families available process-wide, plus the narrow ISO-8859-16
// provider (codepage 28606, which the inbox set lacks).
// Idempotent and additive: already registered providers are kept,
// lookups that worked before resolve identically. G5-bench
// subtitleedit leg (PUSH-1106-vendor).
public static class WasmEncodingBoot
{
    private static int s_registered;

    public static void EnsureRegistered()
    {
        if (System.Threading.Interlocked.CompareExchange(ref s_registered, 1, 0) != 0)
            return;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding.RegisterProvider(System.Text.Iso885916EncodingProvider.Instance);
        }
        catch
        {
            System.Threading.Interlocked.Exchange(ref s_registered, 0);
            throw;
        }
    }
}
