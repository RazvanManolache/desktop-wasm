using System.Diagnostics;
using System.Security.Cryptography;
using WfWasm.CanvasHost;

// RT-NEXT-2033: the CLR probes "<base>.XmlSerializers" for every
// XmlSerializer use. These optional probes must never pay the synchronous
// demand round-trip (RV2944-shell: ~12s serial sync-XHR storm wait, cold
// AND warm); they short-circuit to the same miss a 204 would produce.
public sealed class DemandResolveShortCircuitTests
{
    [Fact]
    public void IsKnownOptionalProbe_XmlSerializersSuffix_Matches()
    {
        Assert.True(AppAssemblyLoader.IsKnownOptionalProbe("Telerik.WinControls.XmlSerializers"));
        Assert.True(AppAssemblyLoader.IsKnownOptionalProbe("System.Xml.XmlSerializers"));
        Assert.True(AppAssemblyLoader.IsKnownOptionalProbe("foo.xmlserializers"));
    }

    [Fact]
    public void IsKnownOptionalProbe_OrdinaryNames_Do_Not_Match()
    {
        Assert.False(AppAssemblyLoader.IsKnownOptionalProbe("Telerik.WinControls.UI"));
        Assert.False(AppAssemblyLoader.IsKnownOptionalProbe("XmlSerializers"));
        Assert.False(AppAssemblyLoader.IsKnownOptionalProbe("Foo.XmlSerializers.Native"));
        Assert.False(AppAssemblyLoader.IsKnownOptionalProbe(""));
    }

    [Fact]
    public void ResolveDemandBytes_XmlSerializers_ShortCircuits_Without_Fetch()
    {
        int calls = 0;
        AppAssemblyLoader.DemandFetch = (app, name) => { calls++; Thread.Sleep(250); return []; };
        try
        {
            var sw = Stopwatch.StartNew();
            byte[]? bytes = AppAssemblyLoader.ResolveDemandBytes("shortcircuit-app", "Telerik.WinControls.XmlSerializers");
            sw.Stop();
            Assert.Null(bytes);
            Assert.Equal(0, calls);
            Assert.True(sw.ElapsedMilliseconds < 100,
                $"Optional probe must not pay the demand round-trip (took {sw.ElapsedMilliseconds}ms, {calls} fetch calls).");
        }
        finally { AppAssemblyLoader.ResetDemandFetchForTest(); }
    }

    [Fact]
    public void ResolveDemandBytes_OrdinaryMiss_Still_Fetches()
    {
        int calls = 0;
        AppAssemblyLoader.DemandFetch = (app, name) => { calls++; return []; };
        try
        {
            Assert.Null(AppAssemblyLoader.ResolveDemandBytes("fetch-app", "Telerik.WinControls.UI"));
            Assert.Equal(1, calls);
        }
        finally { AppAssemblyLoader.ResetDemandFetchForTest(); }
    }

    [Fact]
    public void ResolveDemandBytes_OrdinaryHit_Decodes_Payload()
    {
        byte[] payload = [9, 8, 7, 6, 5];
        byte[] packet = SHA256.HashData(payload).Concat(payload).ToArray();
        AppAssemblyLoader.DemandFetch = (app, name) => packet;
        try
        {
            byte[]? bytes = AppAssemblyLoader.ResolveDemandBytes("hit-app", "Telerik.WinControls.UI");
            Assert.NotNull(bytes);
            Assert.Equal(payload, bytes);
        }
        finally { AppAssemblyLoader.ResetDemandFetchForTest(); }
    }

    [Fact]
    public void OptionalProbeCandidates_Covers_Entry_And_DirectRefs_Without_Dupes()
    {
        var got = AppAssemblyLoader.OptionalProbeCandidates("paintnet", ["Telerik.WinControls.UI", "paintnet"]);
        Assert.True(got.SequenceEqual(["paintnet.XmlSerializers", "Telerik.WinControls.UI.XmlSerializers"]),
            "Probe warm set was: " + string.Join(",", got));
    }
}
