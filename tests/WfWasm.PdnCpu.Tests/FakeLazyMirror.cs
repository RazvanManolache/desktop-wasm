// Faithful shape-double for the real PaintDotNet.Functional lazy universe
// (verified against pristine PaintDotNet.Fundamentals.dll IL): the static
// LazyResult factory class builds the concrete LazyResult`2, while
// LazyResult`1<T> is ABSTRACT with only a famandassem .ctor — so the real
// type can never be instantiated directly. The CpuPaint helper must route
// through the static New<T>(Func<T>) factory. This assembly never ships;
// pristine PDN never loads in-process, so there is no production name
// clash.
namespace PaintDotNet.Functional
{
    internal abstract class FakeLazyMirror<T>
    {
        public abstract T Value { get; }
    }

    internal sealed class FakeLazyMirrorImpl<T> : FakeLazyMirror<T>
    {
        private readonly Func<T> _factory;
        public FakeLazyMirrorImpl(Func<T> factory) => _factory = factory;
        public override T Value => _factory();
    }

    internal static class LazyResult
    {
        public static FakeLazyMirror<T> New<T>(Func<T> valueFactory) =>
            new FakeLazyMirrorImpl<T>(valueFactory);
    }
}
