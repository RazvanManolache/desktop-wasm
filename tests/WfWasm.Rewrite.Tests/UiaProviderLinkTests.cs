using Mono.Cecil;

// Red-first link map for VEN-G3-1927: C1 v10 / Infragistics v8 reference
// UIAutomationProvider (+Types), which is absent from the benchmark inputs,
// so form-create dies in TypeLoad. This test enumerates every referenced
// member the fix (serve/shim) must provide. It FAILS while the provider
// assembly is missing and passes once every referenced member resolves.
public sealed class UiaProviderLinkTests
{
    private static string[] C1Assemblies() => new[]
    {
        "C1.CalcEngine.dll",
        "C1.DataCollection.BindingList.dll",
        "C1.DataCollection.dll",
        "C1.Excel.dll",
        "C1.Win.10.dll",
        "C1.Win.ExpressionEditor.10.dll",
        "C1.Win.FlexGrid.10.dll",
        "C1.Win.FlexGrid.ImportExport.10.dll",
        "C1.Win.Ribbon.10.dll",
        "C1.Win.RulesManager.10.dll",
        "C1.Win.SuperTooltip.10.dll",
        "C1.Win.Themes.10.dll",
        "C1.Zip.dll",
        "Showcase.dll",
    };

    private static string[] IgAssemblies() => new[]
    {
        "IGPaint.dll",
        "Infragistics.Shared.dll",
        "Infragistics.Win.DataVisualization.Shared.dll",
        "Infragistics.Win.DataVisualization.UltraDataChart.dll",
        "Infragistics.Win.dll",
        "Infragistics.Win.Misc.dll",
        "Infragistics.Win.Portable.Core.dll",
        "Infragistics.Win.UltraWinEditors.dll",
        "Infragistics.Win.UltraWinExplorerBar.dll",
        "Infragistics.Win.UltraWinGrid.dll",
        "Infragistics.Win.UltraWinStatusBar.dll",
        "Infragistics.Win.UltraWinTabControl.dll",
        "Infragistics.Win.UltraWinToolbars.dll",
    };

    public static (string assembly, string version, string? pkt, System.Collections.Generic.List<string> members)
        MapUiaRefs(string dir, string[] names)
    {
        string? version = null;
        string? pkt = null;
        string? asm = null;
        var members = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var n in names)
        {
            string path = Path.Combine(dir, n);
            if (!File.Exists(path)) continue;
            using var read = AssemblyDefinition.ReadAssembly(path);
            foreach (var ar in read.MainModule.AssemblyReferences)
            {
                if (ar.Name == "UIAutomationProvider" || ar.Name == "UIAutomationTypes")
                {
                    System.Console.WriteLine($"UIA-REF: {n} -> {ar.Name} VERSION: {ar.Version} PKT: {string.Concat((ar.PublicKeyToken ?? System.Array.Empty<byte>()).Select(b => b.ToString("x2")))}");
                    asm ??= ar.Name;
                    version ??= ar.Version.ToString();
                    var bytes = ar.PublicKeyToken;
                    if (bytes != null && bytes.Length > 0)
                        pkt ??= string.Concat(bytes.Select(b => b.ToString("x2")));
                    foreach (var mr in collectMembers(read, ar.Name))
                        if (seen.Add(ar.Name + "!" + mr))
                            members.Add(ar.Name + "!" + mr);
                }
            }
        }
        return (asm ?? "<none>", version ?? "<none>", pkt, members);
    }

    private static System.Collections.Generic.IEnumerable<string> collectMembers(AssemblyDefinition read, string asmName)
    {
        var stack = new Stack<TypeDefinition>(read.MainModule.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t.BaseType is TypeReference bt && bt.Scope is AssemblyNameReference ab && ab.Name == asmName)
                yield return "base:" + t.FullName + " : " + bt.FullName;
            if (t.HasInterfaces)
                foreach (var ii in t.Interfaces)
                    if (ii.InterfaceType.Scope is AssemblyNameReference ai && ai.Name == asmName)
                        yield return "iface:" + t.FullName + " : " + ii.InterfaceType.FullName;
            if (t.HasFields)
                foreach (var f in t.Fields)
                    if (f.FieldType.Scope is AssemblyNameReference af && af.Name == asmName)
                        yield return "field:" + t.FullName + "." + f.Name + " : " + f.FieldType.FullName;
            if (t.HasMethods)
                foreach (var m in t.Methods)
                {
                    if (m.ReturnType.Scope is AssemblyNameReference ar && ar.Name == asmName)
                        yield return "ret:" + t.FullName + "." + m.Name + " : " + m.ReturnType.FullName;
                    if (m.HasParameters)
                        foreach (var p in m.Parameters)
                            if (p.ParameterType.Scope is AssemblyNameReference ap && ap.Name == asmName)
                                yield return "param:" + t.FullName + "." + m.Name + " : " + p.ParameterType.FullName;
                    if (m.HasBody)
                        foreach (var i in m.Body.Instructions)
                            if (i.Operand is MemberReference opr
                                && opr.DeclaringType?.Scope is AssemblyNameReference ao
                                && ao.Name == asmName)
                                yield return "call:" + opr.DeclaringType.FullName + "." + opr.Name;
                }
            foreach (var nt in t.NestedTypes)
                stack.Push(nt);
        }
    }

    [Fact]
    public void Dump_C1_Uia_Surface()
    {
        var (asm, version, pkt, members) =
            MapUiaRefs(@"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-componentone", C1Assemblies());
        System.Console.WriteLine($"C1-UIA-ASM: {asm} VERSION: {version} PKT: {pkt} MEMBERS: {members.Count}");
        foreach (var m in members.Take(80))
            System.Console.WriteLine("C1-UIA-MEMBER: " + m);
        File.WriteAllLines(@"E:\AI\Code\DesktopWasm\.artifacts\tmp\runs\dwvendor\ven-g3-1927\c1-members.txt", members);
        Assert.NotEmpty(members);
    }

    [Fact]
    public void Dump_IG_Uia_Surface()
    {
        var (asm, version, pkt, members) =
            MapUiaRefs(@"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-infragistics", IgAssemblies());
        System.Console.WriteLine($"IG-UIA-ASM: {asm} VERSION: {version} PKT: {pkt} MEMBERS: {members.Count}");
        foreach (var m in members.Take(80))
            System.Console.WriteLine("IG-UIA-MEMBER: " + m);
        File.WriteAllLines(@"E:\AI\Code\DesktopWasm\.artifacts\tmp\runs\dwvendor\ven-g3-1927\ig-members.txt", members);
        Assert.NotEmpty(members);
    }

    [Fact]
    public void Uia_Shim_Resolves_All_Referenced_Members()
    {
        // Red-first link gate for VEN-G3-1927: every UIAutomation member
        // referenced by the C1/IG stacks must resolve in the served shim
        // assemblies. FAILS while the shims are absent; passes once the
        // codegen'd shims cover the full referenced surface.
        string c1 = @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-componentone\UIAutomationProvider.dll";
        string c1t = @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-componentone\UIAutomationTypes.dll";
        string ig = @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-infragistics\UIAutomationProvider.dll";
        string igt = @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-infragistics\UIAutomationTypes.dll";
        var missing = new System.Collections.Generic.List<string>();
        CheckFile(c1, "UIAutomationProvider", @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-componentone", C1Assemblies(), missing);
        CheckFile(c1t, "UIAutomationTypes", @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-componentone", C1Assemblies(), missing);
        CheckFile(ig, "UIAutomationProvider", @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-infragistics", IgAssemblies(), missing);
        CheckFile(igt, "UIAutomationTypes", @"E:\AI\Code\DesktopWasm\.artifacts\benchmark-apps\benchmark-infragistics", IgAssemblies(), missing);
        Assert.True(missing.Count == 0,
            "unresolved UIA members:\n" + string.Join("\n", missing.Take(40)));
    }

    private static void CheckFile(string shimPath, string asmName, string dir, string[] names,
        System.Collections.Generic.List<string> missing)
    {
        if (!File.Exists(shimPath))
        {
            missing.Add($"{asmName}: shim missing at {shimPath}");
            return;
        }
        using var shim = AssemblyDefinition.ReadAssembly(shimPath);
        var types = new System.Collections.Generic.Dictionary<string, TypeDefinition>();
        var stack = new Stack<TypeDefinition>(shim.MainModule.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            types[t.FullName] = t;
            foreach (var nt in t.NestedTypes)
                stack.Push(nt);
        }
        var (_, _, _, members) = MapUiaRefs(dir, names);
        foreach (var m in members)
        {
            int bang = m.IndexOf('!');
            if (bang < 0) continue;
            if (m.Substring(0, bang) != asmName) continue;
            string spec = m.Substring(bang + 1);
            // spec kinds: base:T : B | iface:T : I | field:T.F : FT |
            // ret:T.M : RT | param:T.M : PT | call:DT.M
            // The wanted UIA type is always the part after " : ", except for
            // call: entries (no " : ") where it is the declaring type.
            string typeName;
            int sep = spec.IndexOf(" : ", StringComparison.Ordinal);
            if (spec.StartsWith("call:", StringComparison.Ordinal))
            {
                string dt = spec.Substring("call:".Length);
                typeName = dt.Substring(0, dt.LastIndexOf('.')).TrimEnd('.');
            }
            else if (sep >= 0)
            {
                typeName = spec.Substring(sep + 3);
            }
            else continue;
            // Array ranks are constructed, not declared: check the element type.
            while (typeName.EndsWith("[]", StringComparison.Ordinal))
                typeName = typeName.Substring(0, typeName.Length - 2);
            // Generic instantiations resolve via their element type.
            int g = typeName.IndexOf('<');
            if (g >= 0) typeName = typeName.Substring(0, g);
            if (!types.ContainsKey(typeName))
                missing.Add($"{asmName}: missing type {typeName} (from {spec})");
        }
    }
}
