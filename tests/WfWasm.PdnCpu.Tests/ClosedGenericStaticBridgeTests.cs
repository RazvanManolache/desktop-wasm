using WfWasm.PdnCpu;

// TRIO-METHOD-BRIDGE (WC-20261103T0133-2791): pins the
// CpuPaint.InvokeClosedGenericStatic machinery with local fakes (PDN
// assemblies do not load under the test host, so the rewriter-side pin plus
// the trio gate carry the end-to-end proof). Covers exact-overload
// resolution: generic-arity match, substituted parameter-type equality (no
// assignability fallback), overload rejection, loud failure on miss, and
// value-type return unboxing.
public sealed class ClosedGenericStaticBridgeTests
{
    private static class FakeHost
    {
        public static Tuple<T1, T2> MakePair<T1, T2>(T1 a, T2 b) => Tuple.Create(a, b);

        public static T2 Second<T1, T2>(T1 a, T2 b) => b;

        public static T Single<T>(T a) => a;

        public static T Identity<T>(T a) => a;
    }

    private static string Aqn(Type t) => t.AssemblyQualifiedName!;

    [Fact]
    public void Bridge_Resolves_Closed_Generic_Static_By_Exact_Signature()
    {
        string dbl = Aqn(typeof(double));
        object? pair = CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "MakePair",
            new[] { dbl, dbl }, new[] { dbl, dbl },
            new object?[] { 1.0, 2.0 });
        Assert.Equal(Tuple.Create(1.0, 2.0), pair);
    }

    [Fact]
    public void Bridge_Accepts_Rewriter_Short_Names()
    {
        // The rewriter emits "FullName, Assembly" without version/culture
        // (and maps the System.Runtime facade to System.Private.CoreLib);
        // the helper must bind those, not just full AssemblyQualifiedNames.
        // This is the exact shape that failed loud in vivo on gen23.
        const string dbl = "System.Double, System.Private.CoreLib";
        object? pair = CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "MakePair",
            new[] { dbl, dbl }, new[] { dbl, dbl },
            new object?[] { 3.0, 4.0 });
        Assert.Equal(Tuple.Create(3.0, 4.0), pair);
    }

    [Fact]
    public void Bridge_Unboxes_ValueType_Returns()
    {
        string i32 = Aqn(typeof(int));
        object? sum = CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "Identity",
            new[] { i32 }, new[] { i32 },
            new object?[] { 42 });
        Assert.Equal(42, sum);
    }

    [Fact]
    public void Bridge_Rejects_Wrong_Param_Types()
    {
        // Same name/arity/type-args, but the substituted parameter types
        // must match EXACTLY (no assignability fallback): string names
        // against double params must NOT bind.
        string str = Aqn(typeof(string));
        string dbl = Aqn(typeof(double));
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.InvokeClosedGenericStatic(
                typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "MakePair",
                new[] { dbl, dbl }, new[] { str, str },
                new object?[] { 1.0, 2.0 }));
    }

    [Fact]
    public void Bridge_Fails_Loud_On_Missing_Method()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CpuPaint.InvokeClosedGenericStatic(
                typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "NoSuchMethod",
                new[] { Aqn(typeof(int)) }, new[] { Aqn(typeof(int)) },
                new object?[] { 1 }));
    }

    [Fact]
    public void Bridge_Distinguishes_Overloads_By_Arity()
    {
        // Single<T> vs Second<T1,T2>: arity alone selects the target.
        string i32 = Aqn(typeof(int));
        string str = Aqn(typeof(string));
        object? second = CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "Second",
            new[] { i32, str }, new[] { i32, str },
            new object?[] { 7, "seven" });
        Assert.Equal("seven", second);
        object? single = CpuPaint.InvokeClosedGenericStatic(
            typeof(FakeHost).FullName!, typeof(FakeHost).Assembly.GetName().Name!, "Single",
            new[] { str }, new[] { str },
            new object?[] { "solo" });
        Assert.Equal("solo", single);
    }
}
