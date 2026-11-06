using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint effect-setting getters must preserve exact dispatch: the
// rewriter replaces direct callvirt on Property<TValue>/
// ISetting<TValue>/IScalarAppSetting<TValue>/VectorProperty<TValue>/
// Result<TValue> getters (which throw InvalidProgramException in the
// browser interpreter on the Twist/Quantize/Bulge/Clouds paths) with
// these reflection helpers. Values, min/max bounds, and the token
// GetProperty factory must read identically through the helper;
// unknown shapes fail LOUD (there is no safe default for a read).
public sealed class EffectSettingGetterTests
{
    private class FakeProperty<T>
    {
        public T Value { get; set; } = default!;
        public T MinValue { get; set; } = default!;
        public T MaxValue { get; set; } = default!;
    }

    private interface IFakeSetting<T>
    {
        T Value { get; }
    }

    private sealed class ExplicitSetting : IFakeSetting<int>
    {
        int IFakeSetting<int>.Value => 41;
    }

    private sealed class FakeResult<T>
    {
        public bool IsValue { get; set; }
        public T Value { get; set; } = default!;
    }

    private class FakeToken
    {
        private readonly Dictionary<string, object> _props = new();

        public void Add(string name, object prop) => _props[name] = prop;

        public object GetProperty(object name) => _props[(string)name];

        public T GetProperty<T>(object name) => (T)GetProperty(name);
    }

    private sealed class ThrowingSetting
    {
        public int Value => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Value_Min_Max_Read_Through_Helper()
    {
        var p = new FakeProperty<int> { Value = 7, MinValue = 1, MaxValue = 9 };
        Assert.Equal(7, CpuPaint.GetEffectSettingValue(p, 0));
        Assert.Equal(1, CpuPaint.GetEffectSettingValue(p, 1));
        Assert.Equal(9, CpuPaint.GetEffectSettingValue(p, 2));
    }

    [Fact]
    public void Double_Value_Unboxes_To_Double()
    {
        var p = new FakeProperty<double> { Value = 2.5 };
        Assert.Equal(2.5, Assert.IsType<double>(CpuPaint.GetEffectSettingValue(p, 0)));
    }

    [Fact]
    public void Explicit_Interface_Getter_Found_Via_Interface_Map()
    {
        Assert.Equal(41, CpuPaint.GetEffectSettingValue(new ExplicitSetting(), 0));
    }

    [Fact]
    public void Result_IsValue_And_Value_Read_Through_Helper()
    {
        var r = new FakeResult<string> { IsValue = true, Value = "x" };
        Assert.True((bool)CpuPaint.GetEffectSettingValue(r, 3));
        Assert.Equal("x", CpuPaint.GetEffectSettingValue(r, 0));
    }

    [Fact]
    public void Token_GetProperty_Returns_Registered_Property()
    {
        var token = new FakeToken();
        var prop = new FakeProperty<int> { Value = 11 };
        token.Add("Amount", prop);
        object got = CpuPaint.GetEffectTokenProperty(token, "Amount");
        Assert.Same(prop, got);
        Assert.Equal(11, CpuPaint.GetEffectSettingValue(got, 0));
    }

    [Fact]
    public void Unknown_Shape_Fails_Loud()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectSettingValue(new object(), 0));
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectTokenProperty(new object(), "x"));
    }

    [Fact]
    public void Token_Invocation_Errors_Pass_Through_Unwrapped()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            CpuPaint.GetEffectTokenProperty(new FakeToken(), "Missing"));
    }

    [Fact]
    public void Getter_Exception_Unwraps_Intact()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectSettingValue(new ThrowingSetting(), 0));
        Assert.Equal("boom", ex.Message);
    }
    // TRIO-NEXTWALL disambiguation: hide-by-name shapes that threw
    // AmbiguousMatchException (Twist/Bulge CpuGpuQualitySetting) resolve
    // exactly once the rewrite site passes the call's declaring type.
    private interface IFakeAmbig<T>
    {
        T Value { get; }
    }

    private class AmbigBase
    {
        public virtual int Value => 1;
    }

    private sealed class AmbigSetting : AmbigBase, IFakeAmbig<int>
    {
        public new int Value => 7;
        int IFakeAmbig<int>.Value => 41;
    }

    private class OverrideBase
    {
        public virtual int Value => 1;
    }

    private sealed class OverrideSetting : OverrideBase
    {
        public override int Value => 9;
    }

    private interface IEmptyFace
    {
        int Missing { get; }
    }

    [Fact]
    public void Legacy_TwoArg_Never_Throws_AmbiguousMatch_On_HideByName()
    {
        // Pre-siteType behavior stays total: most-derived public member
        // wins deterministically (7), no AmbiguousMatch escapes.
        object v = CpuPaint.GetEffectSettingValue(new AmbigSetting(), 0);
        Assert.Equal(7, v);
    }

    [Fact]
    public void Interface_SiteType_Resolves_Via_Map_Not_Name()
    {
        // Exact runtime dispatch for the explicit implementation (41),
        // not the hiding public member (7).
        object v = CpuPaint.GetEffectSettingValue(
            new AmbigSetting(), 0, typeof(IFakeAmbig<int>));
        Assert.Equal(41, v);
    }

    [Fact]
    public void Class_SiteType_Respects_Virtual_Dispatch()
    {
        // Virtual slot: most-derived override wins (9).
        object v = CpuPaint.GetEffectSettingValue(
            new OverrideSetting(), 0, typeof(OverrideBase));
        Assert.Equal(9, v);
        // Virtual slot with `new` (no override): the declarer's own member wins (1), matching runtime fallback to the base implementation.
        object w = CpuPaint.GetEffectSettingValue(
            new AmbigSetting(), 0, typeof(AmbigBase));
        Assert.Equal(1, w);
    }

    [Fact]
    public void Unknown_SiteType_Fails_Loud()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectSettingValue(new AmbigSetting(), 0, typeof(IEmptyFace)));
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectSettingValue(new AmbigSetting(), 0, typeof(string)));
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetEffectSettingValue(null!, 0, typeof(IFakeAmbig<int>)));

    }

    [Fact]
    public void Interface_Bridge_Reads_Enumerator_And_Count()
    {
        var list = new System.Collections.Generic.List<int> { 3, 4 };
        object e = CpuPaint.GetInterfaceMemberValue(
            list, typeof(System.Collections.Generic.IEnumerable<int>), "GetEnumerator");
        Assert.NotNull(e);
        Assert.True((bool)e.GetType().GetMethod("MoveNext")!.Invoke(e, null)!);
        object cur = CpuPaint.GetInterfaceMemberValue(
            e, typeof(System.Collections.Generic.IEnumerator<int>), "get_Current");
        Assert.Equal(3, cur);
        object n = CpuPaint.GetInterfaceMemberValue(
            list, typeof(System.Collections.Generic.IReadOnlyCollection<int>), "get_Count");
        Assert.Equal(2, n);
    }

    [Fact]
    public void IsWindowIntPtr_Matches_Nonzero_Handle_Semantics()
    {
        Assert.Equal(0, CpuPaint.IsWindowIntPtr(System.IntPtr.Zero));
        Assert.Equal(1, CpuPaint.IsWindowIntPtr(new System.IntPtr(0x1000)));
        Assert.Equal(1, CpuPaint.IsWindowIntPtr(new System.IntPtr(0x10001)));
    }
    [Fact]
    public void Interface_Bridge_Rejects_Nulls_Loud()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetInterfaceMemberValue(null!, typeof(System.Collections.IEnumerable), "x"));
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.GetInterfaceMemberValue(new object(), null!, "x"));
    }

    [Fact]
    public void Lazy_Factory_Routes_Through_Static_New_Not_Direct_Construction()
    {
        // Real LazyResult`1<T> is abstract with only a famandassem .ctor
        // (pristine Fundamentals IL); it can never be instantiated directly.
        // The helper must build the concrete product through static
        // LazyResult.New<T>, exactly as desktop PDN does.
        object lazy = CpuPaint.NewEffectLazyResult(
            new Func<string>(() => "q"),
            typeof(PaintDotNet.Functional.FakeLazyMirror<string>));
        var impl = Assert.IsType<PaintDotNet.Functional.FakeLazyMirrorImpl<string>>(lazy);
        Assert.Equal("q", impl.Value);
    }

    // GetEffectSourceBitmap/GetEnumerableValues resolve their PDN host
    // types by AppDomain scan, which cannot run in-process (pristine PDN
    // assemblies do not load under the test host); they are covered by
    // the rewrite pin test plus the staged browser gate.
}
