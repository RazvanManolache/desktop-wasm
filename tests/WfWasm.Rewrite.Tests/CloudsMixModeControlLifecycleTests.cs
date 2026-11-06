using System.Reflection;

// Red-first construct test for the narrowed Clouds window: replicates the
// MixMode control lifecycle from CloudsGpuEffect.OnCreateConfigUI
// (CreateFor + DisplayName/Description/Multiline set_Value + Clone) on
// pristine binaries in an isolated ALC. No host, no Services, no dialog.
// Either outcome is decisive: an NRE here surfaces with full TargetSite +
// frames (the served operand), while green exonerates the control region
// and leaves only the Services/factory/loop span (host-dependent) standing.
public sealed class CloudsMixModeControlLifecycleTests
{
    private const string PaintDir = @"E:\AI\Code\WindowsForms\targets\paint.net";

    [Fact]
    public void MixMode_Control_Lifecycle_Completes_Headless()
    {
        if (!File.Exists(Path.Combine(PaintDir, "PaintDotNet.PropertySystem.dll"))) return;
        var alc = new System.Runtime.Loader.AssemblyLoadContext($"CloudsMix_{Guid.NewGuid()}", true);
        try
        {
            alc.Resolving += (ctx, name) =>
            {
                string probe = Path.Combine(PaintDir, (name.Name ?? "") + ".dll");
                if (File.Exists(probe)) return ctx.LoadFromAssemblyPath(probe);
                return null;
            };
            TouchMixModeLifecycleOnce(alc);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally { alc.Unload(); }
    }

    private static void TouchMixModeLifecycleOnce(System.Runtime.Loader.AssemblyLoadContext alc)
    {
        var psAsm = alc.LoadFromAssemblyPath(Path.Combine(PaintDir, "PaintDotNet.PropertySystem.dll"));
        var coreAsm = alc.LoadFromAssemblyPath(Path.Combine(PaintDir, "PaintDotNet.Core.dll"));
        // Mirror app startup: ControlInfo.Initialize(controlsAssembly) sets
        // the static WinForms-controls assembly (null in a bare host would
        // throw InternalErrorException, not the served NRE). The creator maps
        // are built from PropertyControl subclasses in the app assembly.
        var appAsm = alc.LoadFromAssemblyPath(Path.Combine(PaintDir, "paintdotnet.dll"));
        var ciT = coreAsm.GetType("PaintDotNet.IndirectUI.ControlInfo");
        Assert.NotNull(ciT);
        var init = ciT.GetMethod("Initialize",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(init);
        init.Invoke(null, new object?[] { appAsm });
        var fwAsm = alc.LoadFromAssemblyPath(Path.Combine(PaintDir, "PaintDotNet.Windows.Framework.dll"));
        var resAsm = alc.LoadFromAssemblyPath(Path.Combine(PaintDir, "PaintDotNet.Resources.dll"));

        // MixMode choices exactly like OnCreatePropertyCollection.
        var mixModeUtil = fwAsm.GetType("PaintDotNet.Direct2D1.Effects.MixModeUtil");
        Assert.NotNull(mixModeUtil);
        var getModes = mixModeUtil.GetMethod("GetUserMixModes",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(getModes);
        var modesRaw = getModes.Invoke(null, null)!;
        var modesList = ((System.Collections.IEnumerable)modesRaw).Cast<object>().ToArray();
        Assert.True(modesList.Length > 0);
        var mixModeT = fwAsm.GetType("PaintDotNet.Direct2D1.Effects.MixMode");
        Assert.NotNull(mixModeT);
        object composite = Enum.Parse(mixModeT, "CompositeSourceOver");
        var choices = modesList;
        int defaultIndex = Array.IndexOf(choices, composite);
        Assert.True(defaultIndex >= 0);

        // StaticListChoiceProperty(name, choices, defaultIndex).
        var slcT = psAsm.GetType("PaintDotNet.PropertySystem.StaticListChoiceProperty");
        Assert.NotNull(slcT);
        var slcCtor = slcT.GetConstructors()
            .First(c => c.GetParameters().Length == 3);
        object mixModeProp = slcCtor.Invoke(new object?[] { "MixMode", choices, defaultIndex });
        Assert.NotNull(mixModeProp);

        // PropertyControlInfo.CreateFor(property).
        var pciT = coreAsm.GetType("PaintDotNet.IndirectUI.PropertyControlInfo");
        Assert.NotNull(pciT);
        var createFor = pciT.GetMethod("CreateFor",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(createFor);
        object control = createFor.Invoke(null, new object?[] { mixModeProp })!;
        Assert.NotNull(control);

        // ControlProperties[DisplayName|Description|Multiline] set_Value,
        // mirroring OnCreateConfigUI lines for the MixMode control.
        var bag = control.GetType().GetProperty("ControlProperties")!.GetValue(control);
        Assert.NotNull(bag);
        var cipnT = coreAsm.GetType("PaintDotNet.IndirectUI.ControlInfoPropertyNames");
        Assert.NotNull(cipnT);
        object displayNameKey = Enum.Parse(cipnT, "DisplayName");
        object descriptionKey = Enum.Parse(cipnT, "Description");
        object multilineKey = Enum.Parse(cipnT, "Multiline");
        var getItem = bag.GetType().GetMethod("get_Item");
        Assert.NotNull(getItem);
        object displayProp = getItem.Invoke(bag, new object?[] { displayNameKey })!;
        object descProp = getItem.Invoke(bag, new object?[] { descriptionKey })!;
        object multiProp = getItem.Invoke(bag, new object?[] { multilineKey })!;
        Assert.NotNull(displayProp);
        Assert.NotNull(descProp);
        Assert.NotNull(multiProp);
        var pdnResT = resAsm.GetType("PaintDotNet.Resources.PdnResources");
        Assert.NotNull(pdnResT);
        var getString = pdnResT.GetMethod("GetString", new[] { typeof(string) });
        Assert.NotNull(getString);
        SetTypedValue(displayProp, string.Empty);
        SetTypedValue(descProp, getString.Invoke(null, new object?[] { "CloudsEffect.ConfigDialog.BlendModeHeader.Text" }));
        SetTypedValue(multiProp, false);

        // Clone ( RelayCreateConfigForm clones the built tree ).
        static void SetTypedValue(object target, object? value)
        {
            var prop = target.GetType().GetProperties()
                .First(p => p.Name == "Value" && p.CanWrite
                    && (value == null || p.PropertyType.IsInstanceOfType(value)));
            prop.SetValue(target, value);
        }
        var clone = control.GetType().GetMethod("Clone");
        Assert.NotNull(clone);
        object cloned = clone.Invoke(control, null)!;
        Assert.NotNull(cloned);
        var clonedBag = cloned.GetType().GetProperty("ControlProperties")!.GetValue(cloned);
        Assert.NotNull(clonedBag);
    }
}
