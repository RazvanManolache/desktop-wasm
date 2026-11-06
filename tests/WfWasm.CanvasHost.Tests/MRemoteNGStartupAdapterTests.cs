// MREMOTENG-0155: mRemoteNG startup adapter. Served shape (retained
// source mremoteng/mRemoteNG/UI/Forms/frmMain.cs): FrmMain has NO public
// parameterless ctor (private one), behind the public static Default
// singleton; live w23 Create throws NotSupportedException "requires
// application services". Mirrors GitExtensionsStartupAdapter: an emitted
// fake closure (dynamic assembly named like the vendor assembly) stands
// in for the vendor bytes with the same shape (private ctor + Default
// with a forced-failure switch covering the ctor-fallback path).
using System.Reflection;
using System.Reflection.Emit;
using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class MRemoteNGStartupAdapterTests
{
    [Fact]
    public void TryCreateForm_WrongType_Declines()
    {
        bool ok = MRemoteNGStartupAdapter.TryCreateForm(typeof(object), null, out object? form);
        Assert.False(ok);
        Assert.Null(form);
    }

    [Fact]
    public void TryCreateForm_NullType_Declines()
    {
        bool ok = MRemoteNGStartupAdapter.TryCreateForm(null, null, out object? form);
        Assert.False(ok);
        Assert.Null(form);
    }

    [Fact]
    public void TryCreateForm_FakeDefault_Builds()
    {
        FakeMRemoteNG.FailDefault = false;
        try
        {
            bool ok = MRemoteNGStartupAdapter.TryCreateForm(FakeMRemoteNG.FrmMain, null, out object? form);
            Assert.True(ok);
            Assert.NotNull(form);
            Assert.Equal("mRemoteNG.UI.Forms.FrmMain", form!.GetType().FullName);
            Assert.True(FakeMRemoteNG.IsHostForm(form));
        }
        finally
        {
            FakeMRemoteNG.FailDefault = false;
        }
    }

    [Fact]
    public void TryCreateForm_DefaultFailure_Falls_Back_To_Private_Ctor()
    {
        FakeMRemoteNG.FailDefault = true;
        try
        {
            bool ok = MRemoteNGStartupAdapter.TryCreateForm(FakeMRemoteNG.FrmMain, null, out object? form);
            Assert.True(ok);
            Assert.NotNull(form);
            Assert.True(FakeMRemoteNG.IsHostForm(form));
        }
        finally
        {
            FakeMRemoteNG.FailDefault = false;
        }
    }

    [Fact]
    public void TryCreateForm_EnsuresSystemEventsBridgeSubscription()
    {
        // CREDIT1300-RT5 host wiring: consulting the adapter (any outcome,
        // even a decline) ensures the SystemEvents bridge subscription.
        bool ok = MRemoteNGStartupAdapter.TryCreateForm(null, null, out object? form);
        Assert.False(ok);
        Assert.Null(form);
        Assert.True(SystemEventsBridgeAdapter.IsSubscribed);
    }

    [Fact]
    public void TryCreateForm_DegradedBlank_Declines_With_Skip()
    {
        // RVW3293 served seam (live w27/w28): guarded-ctor construction
        // succeeds but yields a blank window (Controls=0) with follow-on
        // NREs in Load. The adapter must decline the degraded form so the
        // harness keeps today's chain instead of serving the blank + NRE.
        FakeMRemoteNG.FailDegraded = true;
        try
        {
            bool ok = MRemoteNGStartupAdapter.TryCreateForm(FakeMRemoteNG.FrmMain, null, out object? form);
            Assert.False(ok);
            Assert.Null(form);
        }
        finally
        {
            FakeMRemoteNG.FailDegraded = false;
        }
    }

    [Fact]
    public void TryCreateForm_DegradedBlank_Private_Ctor_Path_Declines()
    {
        // Same degraded shape through the private-ctor fallback arm
        // (Default forced to fail, fallback ctor builds blank).
        FakeMRemoteNG.FailDefault = true;
        FakeMRemoteNG.FailDegraded = true;
        try
        {
            bool ok = MRemoteNGStartupAdapter.TryCreateForm(FakeMRemoteNG.FrmMain, null, out object? form);
            Assert.False(ok);
            Assert.Null(form);
        }
        finally
        {
            FakeMRemoteNG.FailDefault = false;
            FakeMRemoteNG.FailDegraded = false;
        }
    }

    [Fact]
    public void Create_MremotengBenchmark_Uses_Startup_Adapter_Path()
    {
        // Served red (live w23): Create throws NotSupportedException
        // "requires application services". Green here: the marker routes
        // the emitted fake closure through the adapter branch.
        FakeMRemoteNG.FailDefault = false;
        try
        {
            var benchmark = DesktopBenchmarks.Find(AppScene.BenchmarkMRemoteNg);
            Assert.NotNull(benchmark);
            Assert.Equal("mRemoteNG", benchmark!.StartupAdapter);
            object form = DesktopBenchmarkForms.Create(benchmark);
            Assert.Equal("mRemoteNG.UI.Forms.FrmMain", form.GetType().FullName);
            Assert.True(FakeMRemoteNG.IsHostForm(form));
        }
        finally
        {
            FakeMRemoteNG.FailDefault = false;
        }
    }

    [Fact]
    public void Mremoteng_Marker_Is_Routed_And_Others_Untouched()
    {
        var mremoteng = DesktopBenchmarks.Find(AppScene.BenchmarkMRemoteNg);
        Assert.NotNull(mremoteng);
        Assert.Equal("mRemoteNG", mremoteng!.StartupAdapter);
        foreach (var entry in DesktopBenchmarks.Entries)
        {
            if (entry.Scene == AppScene.BenchmarkMRemoteNg)
                continue;
            if (entry.Scene == AppScene.BenchmarkGitExtensions)
            {
                Assert.Equal("GitExtensions", entry.StartupAdapter);
                continue;
            }
            Assert.Null(entry.StartupAdapter);
        }
    }

    private static class FakeMRemoteNG
    {
        private static Type? _frmMain;
        private static FieldInfo? _failDefault;
        private static FieldInfo? _failDegraded;

        static FakeMRemoteNG()
        {
            Type hostForm = HostFormType();
            ConstructorInfo hostCtor = hostForm.GetConstructor(Type.EmptyTypes)!;
            Assert.NotNull(hostCtor);
            var asm = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("mRemoteNG"), AssemblyBuilderAccess.Run);
            ModuleBuilder mod = asm.DefineDynamicModule("mRemoteNG");
            TypeBuilder fb = mod.DefineType("mRemoteNG.UI.Forms.FrmMain",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                hostForm);
            Type? panelType = hostForm.Assembly.GetType("System.Windows.Forms.Panel");
            Assert.NotNull(panelType);
            ConstructorInfo panelCtor = panelType.GetConstructor(Type.EmptyTypes)!;
            Assert.NotNull(panelCtor);
            System.Reflection.MethodInfo controlsGetter = hostForm.GetProperty("Controls")!.GetMethod!;
            Assert.NotNull(controlsGetter);
            Type? controlType = hostForm.Assembly.GetType("System.Windows.Forms.Control");
            Assert.NotNull(controlType);
            System.Reflection.MethodInfo controlsAdd =
                controlsGetter.ReturnType.GetMethod("Add", new[] { controlType })!;
            Assert.NotNull(controlsAdd);
            FieldBuilder failDegraded = fb.DefineField("FailDegraded",
                typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
            ConstructorBuilder privateCtor = fb.DefineConstructor(
                MethodAttributes.Private, CallingConventions.Standard, Type.EmptyTypes);
            ILGenerator ctorIl = privateCtor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, hostCtor);
            // RVW3293: healthy construction adds one real child control
            // (mirrors FrmMain's msMain/tsContainer/pnlDock); degraded
            // construction (wasm guarded-ctor stash + degraded return)
            // yields zero top-level controls.
            System.Reflection.Emit.Label healthy = ctorIl.DefineLabel();
            ctorIl.Emit(OpCodes.Ldsfld, failDegraded);
            ctorIl.Emit(OpCodes.Brfalse_S, healthy);
            ctorIl.Emit(OpCodes.Ret);
            ctorIl.MarkLabel(healthy);
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Callvirt, controlsGetter);
            ctorIl.Emit(OpCodes.Newobj, panelCtor);
            ctorIl.Emit(OpCodes.Callvirt, controlsAdd);
            ctorIl.Emit(OpCodes.Ret);
            FieldBuilder failDefault = fb.DefineField("FailDefault",
                typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
            PropertyBuilder prop = fb.DefineProperty("Default", PropertyAttributes.None, fb, null);
            MethodBuilder getter = fb.DefineMethod("get_Default",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                fb, Type.EmptyTypes);
            ILGenerator il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, failDefault);
            System.Reflection.Emit.Label ok = il.DefineLabel();
            il.Emit(OpCodes.Brfalse_S, ok);
            il.Emit(OpCodes.Ldstr, "forced Default failure");
            ConstructorInfo boom = typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!;
            il.Emit(OpCodes.Newobj, boom);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(ok);
            il.Emit(OpCodes.Newobj, privateCtor);
            il.Emit(OpCodes.Ret);
            prop.SetGetMethod(getter);
            _frmMain = fb.CreateType()!;
            _failDefault = _frmMain.GetField("FailDefault",
                BindingFlags.Public | BindingFlags.Static)!;
            _failDegraded = _frmMain.GetField("FailDegraded",
                BindingFlags.Public | BindingFlags.Static)!;
        }

        public static Type FrmMain => _frmMain!;

        public static bool FailDefault
        {
            get => (bool)_failDefault!.GetValue(null)!;
            set => _failDefault!.SetValue(null, value);
        }

        public static bool FailDegraded
        {
            get => (bool)_failDegraded!.GetValue(null)!;
            set => _failDegraded!.SetValue(null, value);
        }

        public static bool IsHostForm(object form) =>
            HostFormType().IsInstanceOfType(form);

        private static Type HostFormType()
        {
            // The host's fork Form without a static reference (which would be
            // ambiguous with desktop System.Windows.Forms in test code).
            Assembly? fork = null;
            try
            {
                fork = Assembly.Load("WfWasm.WinForms");
            }
            catch
            {
                fork = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name,
                        "WfWasm.WinForms", StringComparison.Ordinal));
            }
            Assert.NotNull(fork);
            Type? form = fork.GetType("System.Windows.Forms.Form");
            Assert.NotNull(form);
            return form;
        }
    }
}
