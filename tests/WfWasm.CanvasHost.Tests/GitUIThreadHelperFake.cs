namespace GitUI;

// Test fake mirroring the REAL vendor shape proven by ilspycmd on the staged
// GitExtUtils.dll (see IMP-G6B-2120): namespace GitUI (declared in assembly
// GitExtUtils), public getter, INTERNAL setter, HasJoinableTaskContext flag.
// An earlier fake used the wrong namespace, which is why the suite stayed
// green while the production lookup string could never resolve.
internal sealed class ThreadHelperContext
{
}

internal static class ThreadHelper
{
    private static ThreadHelperContext? _context;

    public static bool HasJoinableTaskContext => _context is not null;

    public static ThreadHelperContext JoinableTaskContext
    {
        get => _context ?? throw new InvalidOperationException("not initialized");
        internal set => _context = value;
    }

    public static void Reset() => _context = null;
}
