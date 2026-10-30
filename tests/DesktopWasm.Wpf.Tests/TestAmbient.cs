using System.Reflection;
using DesktopWasm.Core;
using Xunit;
using Xunit.Sdk;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Ambient-state snapshot/restore for test hermeticity (directive item 2).
/// Several engine surfaces are process-wide singletons mutated by tests
/// (WasmDispatcher.Current, Application.Current, XamlLoader.CustomElementSource,
/// MessageBox.Presenter, Keyboard.PrimaryDevice.Modifiers, WpfInputManager held
/// keys, animation/splash clocks). <see cref="ResetAmbientStateAttribute"/>
/// captures them before each test and restores them after, so execution order
/// cannot leak state between tests.
/// </summary>
internal static class TestAmbient
{
    internal sealed class Snapshot
    {
        public WasmDispatcher Dispatcher = null!;
        public Application? App;
        public System.Reflection.Assembly? CustomElementSource;
        public Func<Window, WpfSize, MessageBoxResult>? Presenter;
        public ModifierKeys Modifiers;
    }

    internal static Snapshot Capture()
    {
        // A stale pump (owned by a finished test's threadpool thread) would
        // block Dispatcher.Invoke forever: Send waits for an owner that never
        // pumps again. Rebind to a fresh pump owned by this thread so every
        // test starts with a usable dispatcher; tests that set their own
        // Current (the common interaction-test pattern) overwrite this.
        if (!WasmDispatcher.Current.CheckAccess())
            WasmDispatcher.Current = new WasmDispatcher(new WasmDispatcherPump());
        return new()
        {
            Dispatcher = WasmDispatcher.Current,
            App = Application.Current,
            CustomElementSource = XamlLoader.CustomElementSource,
            Presenter = MessageBox.Presenter,
            Modifiers = Keyboard.PrimaryDevice.Modifiers,
        };
    }

    internal static void Restore(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        WpfInputManager.ResetForTests();
        Keyboard.PrimaryDevice.Modifiers = snapshot.Modifiers;
        MessageBox.Presenter = snapshot.Presenter;
        XamlLoader.CustomElementSource = snapshot.CustomElementSource;
        Application.Current = snapshot.App;
        WasmDispatcher.Current = snapshot.Dispatcher;
        WpfAnimationClock.ResetForTests();
        SplashScreen.ResetForTests();
        System.Configuration.ConfigurationManager.ResetAppSettingsForTests();
    }
}

/// <summary>
/// xUnit hook (see <see cref="TestAmbient"/>): hermetic per-test ambient
/// state for every decorated test class, independent of execution order.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class ResetAmbientStateAttribute : BeforeAfterTestAttribute
{
    private static readonly AsyncLocal<TestAmbient.Snapshot?> _slot = new();

    public override void Before(MethodInfo methodUnderTest) =>
        _slot.Value = TestAmbient.Capture();

    public override void After(MethodInfo methodUnderTest)
    {
        var snapshot = _slot.Value;
        _slot.Value = null;
        if (snapshot is not null)
            TestAmbient.Restore(snapshot);
    }
}
