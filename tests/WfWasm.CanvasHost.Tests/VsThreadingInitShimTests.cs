using System.Reflection;
using System.Reflection.Emit;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// G5-bench gitextensions: the harness skips Program.Main, so the app's
// JoinableTaskContext init never runs. VsThreadingInitShim performs it
// reflectively. The GitUI.ThreadHelper fake (GitUIThreadHelperFake.cs)
// mirrors the REAL vendor shape (namespace GitUI in assembly GitExtUtils,
// public getter, internal setter); the legacy FakeThreadHelper below keeps
// the original same-assembly negatives passing.
public sealed class VsThreadingInitShimTests
{
    [Fact]
    public void RealShape_Uninitialized_GitUI_Helper_Gets_Initialized()
    {
        GitUI.ThreadHelper.Reset();
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(GitUI.ThreadHelper).Assembly, "GitUI.ThreadHelper"));
        Assert.True(GitUI.ThreadHelper.HasJoinableTaskContext);
        GitUI.ThreadHelper.Reset();
    }

    [Fact]
    public void RealShape_Already_Initialized_GitUI_Helper_Stays_Initialized()
    {
        GitUI.ThreadHelper.Reset();
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(GitUI.ThreadHelper).Assembly, "GitUI.ThreadHelper"));
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(GitUI.ThreadHelper).Assembly, "GitUI.ThreadHelper"));
        Assert.True(GitUI.ThreadHelper.HasJoinableTaskContext);
        GitUI.ThreadHelper.Reset();
    }

    [Fact]
    public void EnsureInitialized_Resolves_Real_GitUI_Shape_In_GitExtUtils_Assembly()
    {
        // The production defect: EnsureInitialized hardcoded the
        // "GitExtUtils.ThreadHelper" lookup string, which can never resolve
        // (real type is GitUI.ThreadHelper). A dynamic assembly named
        // GitExtUtils carrying the real shape goes red without the fix.
        Assembly gitExtUtils = BuildGitExtUtilsStub();
        Assert.True(VsThreadingInitShim.EnsureInitialized(gitExtUtils));
    }

    private static Assembly BuildGitExtUtilsStub()
    {
        var asmName = new AssemblyName("GitExtUtils");
        var ab = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
        var mb = ab.DefineDynamicModule("GitExtUtils");
        Type ctxT = mb.DefineType("GitUI.ThreadHelperContext",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed).CreateType()!;
        var tb = mb.DefineType("GitUI.ThreadHelper",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);
        FieldBuilder fld = tb.DefineField("_context", ctxT, FieldAttributes.Private | FieldAttributes.Static);
        MethodBuilder hasGet = tb.DefineMethod("get_HasJoinableTaskContext",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(bool), Type.EmptyTypes);
        ILGenerator hasIl = hasGet.GetILGenerator();
        hasIl.Emit(OpCodes.Ldsfld, fld);
        hasIl.Emit(OpCodes.Ldnull);
        hasIl.Emit(OpCodes.Cgt_Un);
        hasIl.Emit(OpCodes.Ret);
        tb.DefineProperty("HasJoinableTaskContext", PropertyAttributes.None, typeof(bool), null).SetGetMethod(hasGet);
        MethodBuilder get = tb.DefineMethod("get_JoinableTaskContext",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            ctxT, Type.EmptyTypes);
        ILGenerator getIl = get.GetILGenerator();
        getIl.Emit(OpCodes.Ldsfld, fld);
        getIl.Emit(OpCodes.Dup);
        Label ok = getIl.DefineLabel();
        getIl.Emit(OpCodes.Brtrue_S, ok);
        getIl.Emit(OpCodes.Pop);
        getIl.Emit(OpCodes.Ldstr, "not initialized");
        getIl.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        getIl.Emit(OpCodes.Throw);
        getIl.MarkLabel(ok);
        getIl.Emit(OpCodes.Ret);
        MethodBuilder set = tb.DefineMethod("set_JoinableTaskContext",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null, [ctxT]);
        ILGenerator setIl = set.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Stsfld, fld);
        setIl.Emit(OpCodes.Ret);
        PropertyBuilder prop = tb.DefineProperty("JoinableTaskContext", PropertyAttributes.None, ctxT, null);
        prop.SetGetMethod(get);
        prop.SetSetMethod(set);
        tb.CreateType();
        return ab;
    }

    [Fact]
    public void Missing_Type_Returns_False()
    {
        Assert.False(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(VsThreadingInitShimTests).Assembly, "No.Such.Helper"));
    }

    [Fact]
    public void Uninitialized_Fake_Gets_Initialized()
    {
        FakeThreadHelper.Reset();
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(VsThreadingInitShimTests).Assembly, "WfWasm.CanvasHost.Tests.FakeThreadHelper"));
        Assert.True(FakeThreadHelper.HasJoinableTaskContext);
    }

    [Fact]
    public void Already_Initialized_Fake_Stays_Initialized()
    {
        FakeThreadHelper.Reset();
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(VsThreadingInitShimTests).Assembly, "WfWasm.CanvasHost.Tests.FakeThreadHelper"));
        Assert.True(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(VsThreadingInitShimTests).Assembly, "WfWasm.CanvasHost.Tests.FakeThreadHelper"));
        Assert.True(FakeThreadHelper.HasJoinableTaskContext);
    }

    [Fact]
    public void ReadOnly_Helper_Returns_False()
    {
        Assert.False(VsThreadingInitShim.EnsureThreadHelperInitialized(
            typeof(VsThreadingInitShimTests).Assembly, "WfWasm.CanvasHost.Tests.ReadOnlyHelper"));
    }

    [Fact]
    public void Unrelated_Assembly_Returns_False()
    {
        Assert.False(VsThreadingInitShim.EnsureInitialized(typeof(object).Assembly));
    }
}

internal sealed class FakeThreadHelperContext
{
}

internal static class FakeThreadHelper
{
    private static FakeThreadHelperContext? _context;

    public static bool HasJoinableTaskContext => _context is not null;

    public static FakeThreadHelperContext JoinableTaskContext
    {
        get => _context ?? throw new InvalidOperationException("not initialized");
        internal set => _context = value;
    }

    public static void Reset() => _context = null;
}

internal static class ReadOnlyHelper
{
    public static bool HasJoinableTaskContext => false;

    public static object JoinableTaskContext => new();
}
