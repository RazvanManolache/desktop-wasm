namespace WfWasm.WinForms.Tests;

// G5-bench subtitleedit: apps querying legacy codepages hit
// NotSupportedException headless (no provider registered), cascading
// into NRE/IndexOutOfRange follow-ons. WasmEncodingBoot registers the
// inbox CodePages provider once. Idempotent; already-available
// encodings resolve identically (additive).
// NOTE: codepage 28606 (ISO-8859-16, subtitleedit's startup query) is
// NOT in the BCL CodePages set (verified: 28591/28605/1252 resolve,
// 28606 throws even after registration — see encprobe run). It needs a
// custom EncodingProvider; tracked as the subtitleedit follow-up, not
// asserted here.
public sealed class WasmEncodingBootTests
{
    [Fact]
    public void EnsureRegistered_Makes_Legacy_Codepages_Available()
    {
        System.Windows.Forms.WasmEncodingBoot.EnsureRegistered();
        Assert.Equal(28591, System.Text.Encoding.GetEncoding(28591).CodePage);
        Assert.Equal(1252, System.Text.Encoding.GetEncoding(1252).CodePage);
    }

    [Fact]
    public void EnsureRegistered_Is_Idempotent()
    {
        System.Windows.Forms.WasmEncodingBoot.EnsureRegistered();
        var ex = Record.Exception(System.Windows.Forms.WasmEncodingBoot.EnsureRegistered);
        Assert.Null(ex);
        Assert.Equal(28591, System.Text.Encoding.GetEncoding(28591).CodePage);
    }

    [Fact]
    public void Existing_Encodings_Still_Resolve()
    {
        System.Windows.Forms.WasmEncodingBoot.EnsureRegistered();
        Assert.Same(System.Text.Encoding.UTF8, System.Text.Encoding.GetEncoding("utf-8"));
    }
}
