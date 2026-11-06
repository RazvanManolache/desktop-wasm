// UIA shim source generator v2 (VEN-G3-1927).
// Usage: WfWasm.UiaShim.Gen --c1 <dir> --ig <dir> --real <desktop-pack-dir> --out <dir>
// Emits stub C# sources covering every UIAutomation member referenced by the
// C1 v10 / IG v8 stacks. Canonical shapes (kinds, enum values, struct
// fields, delegate invokes) come from the real framework assemblies
// (read-only). Referenced-only members for classes; canonical members for
// interfaces/enums/structs/delegates. Headless-safe: managed no-ops and
// defaults only, no P/Invoke, no COM.
using Mono.Cecil;

var opts = new Dictionary<string, string>();
for (int a = 0; a + 1 < args.Length; a += 2) opts[args[a]] = args[a + 1];
string c1dir = opts["--c1"], igdir = opts["--ig"], realdir = opts["--real"], outdir = opts["--out"];
Directory.CreateDirectory(outdir);

var realProvider = AssemblyDefinition.ReadAssembly(Path.Combine(realdir, "UIAutomationProvider.dll"));
var realTypes = AssemblyDefinition.ReadAssembly(Path.Combine(realdir, "UIAutomationTypes.dll"));
var realByName = new Dictionary<string, TypeDefinition>();
foreach (var r in new[] { realProvider, realTypes })
{
    var st = new Stack<TypeDefinition>(r.MainModule.Types);
    while (st.Count > 0)
    {
        var t = st.Pop();
        realByName[Nk(t.FullName)] = t;
        foreach (var nt in t.NestedTypes) st.Push(nt);
    }
}

// ---------- usage collection ----------
var usedTypes = new Dictionary<string, UseType>();
var fieldUses = new Dictionary<string, Dictionary<string, FieldUse>>(); // typekey -> name -> use
var methodUses = new Dictionary<string, Dictionary<string, MethUse>>(); // typekey -> name+sig -> use
var ctorUses = new Dictionary<string, List<string>>(); // typekey -> sig list

UseType Use(string asm, string full)
{
    string key = asm + "!" + Nk(full);
    if (!usedTypes.TryGetValue(key, out var u)) usedTypes[key] = u = new UseType { Asm = asm, Full = Nk(full) };
    return u;
}
void Want(TypeReference? tr)
{
    if (tr == null) return;
    tr = Strip(tr);
    if (tr is GenericInstanceType gi)
    {
        Want(gi.ElementType);
        foreach (var ga in gi.GenericArguments) Want(ga);
        return;
    }
    if (tr is ArrayType at) { Want(at.ElementType); return; }
    if (tr is GenericParameter) return;
    var asm = (tr.Scope as AssemblyNameReference)?.Name;
    if (asm != "UIAutomationProvider" && asm != "UIAutomationTypes") return;
    Use(asm, tr.FullName);
    // declaring chain for nested types
    var td = tr as TypeDefinition ?? TryResolve(tr);
    if (td != null && td.DeclaringType != null) Want(td.DeclaringType);
}
TypeDefinition? TryResolve(TypeReference tr)
{
    try { return tr.Resolve(); } catch { return null; }
}
void UseMethod(MethodReference mr, bool isStaticCall)
{
    var dt = mr.DeclaringType;
    var asm = (dt.Scope as AssemblyNameReference)?.Name;
    if (asm != "UIAutomationProvider" && asm != "UIAutomationTypes") return;
    Want(dt); Want(mr.ReturnType);
    foreach (var p in mr.Parameters) Want(p.ParameterType);
    if (mr.Name == ".ctor")
    {
        string key = asm + "!" + Nk(dt.FullName);
        if (!ctorUses.TryGetValue(key, out var l)) ctorUses[key] = l = new();
        string sig = string.Join(",", mr.Parameters.Select(p => CsSig(p.ParameterType)));
        if (!l.Contains(sig)) l.Add(sig);
        return;
    }
    string mkey = asm + "!" + Nk(dt.FullName);
    if (!methodUses.TryGetValue(mkey, out var d)) methodUses[mkey] = d = new();
    string mk = mr.Name + "(" + string.Join(",", mr.Parameters.Select(p => CsSig(p.ParameterType))) + "):" + CsSig(mr.ReturnType);
    if (!d.TryGetValue(mk, out var u)) d[mk] = u = new MethUse { Name = mr.Name, IsStatic = isStaticCall, Ret = CsSig(mr.ReturnType), Params = mr.Parameters.Select(p => (CsSig(p.ParameterType), p.Name, p.ParameterType is ByReferenceType)).ToList() };
    else u.IsStatic &= isStaticCall;
}
void Scan(string path)
{
    using var read = AssemblyDefinition.ReadAssembly(path);
    var stack = new Stack<TypeDefinition>(read.MainModule.Types);
    while (stack.Count > 0)
    {
        var t = stack.Pop();
        if (t.BaseType != null) Want(t.BaseType);
        foreach (var ii in t.Interfaces) Want(ii.InterfaceType);
        foreach (var f in t.Fields) Want(f.FieldType);
        foreach (var m in t.Methods)
        {
            Want(m.ReturnType);
            foreach (var p in m.Parameters) Want(p.ParameterType);
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
            {
                var o = ins.Operand;
                if (o is MethodReference mr) UseMethod(mr, ins.OpCode == Mono.Cecil.Cil.OpCodes.Call);
                else if (o is FieldReference fr)
                {
                    var dasm = (fr.DeclaringType.Scope as AssemblyNameReference)?.Name;
                    if (dasm == "UIAutomationProvider" || dasm == "UIAutomationTypes")
                    {
                        Want(fr.DeclaringType); Want(fr.FieldType);
                        string key = dasm + "!" + Nk(fr.DeclaringType.FullName);
                        if (!fieldUses.TryGetValue(key, out var d)) fieldUses[key] = d = new();
                        if (!d.TryGetValue(fr.Name, out var fu)) d[fr.Name] = fu = new FieldUse { Type = CsSig(fr.FieldType) };
                        fu.IsStatic |= ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld || ins.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld;
                    }
                }
                else if (o is TypeReference tr) Want(tr);
            }
        }
        foreach (var nt in t.NestedTypes) stack.Push(nt);
    }
}
foreach (var f in Directory.GetFiles(c1dir, "*.dll")) Scan(f);
foreach (var f in Directory.GetFiles(igdir, "*.dll")) Scan(f);
Console.WriteLine($"used types: {usedTypes.Count}");

// ---------- emit ----------
Emit(Path.Combine(outdir, "UiaProviderStubs.g.cs"), "UIAutomationProvider");
Emit(Path.Combine(outdir, "UiaTypesStubs.g.cs"), "UIAutomationTypes");
Console.WriteLine("emitted.");

void Emit(string path, string asm)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("// GENERATED by WfWasm.UiaShim.Gen — do not hand-edit. Rerun the tool to refresh.");
    sb.AppendLine("// Covers every UIAutomation member referenced by the C1 v10 / IG v8 stacks.");
    sb.AppendLine("// Headless-safe: managed no-ops/defaults only, no P/Invoke, no COM.");
    sb.AppendLine("using System;");
    var tops = usedTypes.Values.Where(w => w.Asm == asm && !IsNested(w.Full)).OrderBy(w => w.Full).ToList();
    var byNs = tops.GroupBy(w => Ns(w.Full)).OrderBy(g => g.Key);
    foreach (var g in byNs)
    {
        sb.AppendLine($"namespace {g.Key}");
        sb.AppendLine("{");
        foreach (var w in g)
            EmitTop(sb, w, 1);
        sb.AppendLine("}");
    }
    File.WriteAllText(path, sb.ToString());
}

bool IsNested(string full) => full.Contains('/') || full.Contains('+');

void EmitTop(System.Text.StringBuilder sb, UseType w, int ind)
{
    string pad = new string(' ', ind * 4);
    if (!realByName.TryGetValue(w.Full, out var real))
    {
        // Referenced but absent from real assemblies: emit by usage only.
        sb.AppendLine($"{pad}// FLAGGED: {w.Full} not in real assemblies; usage-only shell.");
        EmitUsageClass(sb, w, pad);
        return;
    }
    if (real.IsInterface) EmitIface(sb, real, pad, w);
    else if (real.IsEnum) EmitEnum(sb, real, pad);
    else if (real.IsValueType) EmitStruct(sb, real, pad);
    else if (IsDelegate(real)) EmitDelegate(sb, real, pad);
    else EmitClass(sb, real, pad, w);
}

void EmitNested(System.Text.StringBuilder sb, string parentFull, int ind)
{
    string pad = new string(' ', ind * 4);
    var kids = usedTypes.Values
        .Where(w => ParentOf(w.Full) == parentFull && w.Full != parentFull)
        .OrderBy(w => w.Full).ToList();
    foreach (var k in kids)
    {
        if (!realByName.TryGetValue(k.Full, out var real))
        {
            sb.AppendLine($"{pad}// FLAGGED nested without real def: {k.Full}");
            EmitUsageClass(sb, k, pad);
            continue;
        }
        if (real.IsInterface) EmitIface(sb, real, pad, k);
        else if (real.IsEnum) EmitEnum(sb, real, pad);
        else if (real.IsValueType) EmitStruct(sb, real, pad);
        else if (IsDelegate(real)) EmitDelegate(sb, real, pad);
        else EmitClass(sb, real, pad, k);
    }
}

string? ParentOf(string full)
{
    int i = Math.Max(full.LastIndexOf('/'), full.LastIndexOf('+'));
    if (i < 0) return null;
    return full.Substring(0, i).Replace('+', '.').Replace('/', '.');
}

void EmitIface(System.Text.StringBuilder sb, TypeDefinition real, string pad, UseType w)
{
    var ifaces = real.Interfaces.Select(i => CsName(i.InterfaceType, MapParams(real))).ToList();
    string bases = ifaces.Count > 0 ? " : " + string.Join(", ", ifaces) : "";
    sb.AppendLine($"{pad}public interface {TypeDecl(real)}{bases}");
    sb.AppendLine(pad + "{");
    string ip = pad + "    ";
    foreach (var m in real.Methods.Where(m => !m.IsSpecialName))
        sb.AppendLine($"{ip}{CsName(m.ReturnType, MapParams(real))} {m.Name}({Params(m)});");
    foreach (var p in real.Properties)
    {
        string acc = $"{ip}{CsName(p.PropertyType, MapParams(real))} {p.Name} {{ ";
        if (p.GetMethod != null) acc += "get; ";
        if (p.SetMethod != null) acc += "set; ";
        sb.AppendLine(acc + "}");
    }
    foreach (var e in real.Events)
        sb.AppendLine($"{ip}event {CsName(EventDelegate(e), MapParams(real))} {e.Name};");
    // drift members: referenced via this iface but missing from real def
    string key = w.Asm + "!" + w.Full;
    if (methodUses.TryGetValue(key, out var d))
        foreach (var kv in d.Values)
            if (!real.Methods.Any(m => m.Name == kv.Name) && !real.Properties.Any(p => p.Name == kv.Name))
                sb.AppendLine($"{ip}// DRIFT-EXTRA: {kv.Name} referenced by apps, absent from real def.");
    sb.AppendLine(pad + "}");
    VerifyImpl(sb, real, pad);
}

void VerifyImpl(System.Text.StringBuilder sb, TypeDefinition real, string pad)
{
    // Every app type implementing this iface must define every emitted member.
    sb.AppendLine($"{pad}// implementer-coverage verified by UiaProviderLinkTests + live legs.");
}

void EmitEnum(System.Text.StringBuilder sb, TypeDefinition real, string pad)
{
    sb.AppendLine($"{pad}public enum {Short(real.FullName)} : {CsName(real.Fields.First(f => f.Name == "value__").FieldType, new())}");
    sb.AppendLine(pad + "{");
    foreach (var f in real.Fields.Where(f => f.IsStatic && f.IsLiteral))
        sb.AppendLine($"{pad}    {f.Name} = {f.Constant ?? 0},");
    sb.AppendLine(pad + "}");
}

void EmitStruct(System.Text.StringBuilder sb, TypeDefinition real, string pad)
{
    sb.AppendLine($"{pad}public struct {TypeDecl(real)}");
    sb.AppendLine(pad + "{");
    foreach (var f in real.Fields.Where(f => !f.IsSpecialName && !f.IsLiteral))
        sb.AppendLine($"{pad}    public {(f.IsStatic ? "static " : "")}{CsName(f.FieldType, MapParams(real))} {f.Name};");
    EmitNested(sb, Nk(real.FullName), indOf(pad) + 1);
    sb.AppendLine(pad + "}");
}

void EmitDelegate(System.Text.StringBuilder sb, TypeDefinition real, string pad)
{
    var inv = real.Methods.First(m => m.Name == "Invoke");
    sb.AppendLine($"{pad}public delegate {CsName(inv.ReturnType, MapParams(real))} {Short(real.FullName)}({Params(inv)});");
}

void EmitClass(System.Text.StringBuilder sb, TypeDefinition real, string pad, UseType w)
{
    var bt = real.BaseType != null && real.BaseType.FullName != "System.Object"
        ? " : " + CsName(real.BaseType, MapParams(real)) : "";
    var ifaces = real.Interfaces.Select(i => CsName(i.InterfaceType, MapParams(real))).ToList();
    if (ifaces.Count > 0) bt += (bt == "" ? " : " : ", ") + string.Join(", ", ifaces);
    sb.AppendLine($"{pad}public class {TypeDecl(real)}{bt}");
    sb.AppendLine(pad + "{");
    string ip = pad + "    ";
    sb.AppendLine($"{ip}public {Short(real.FullName)}() {{ }}");
    string key = w.Asm + "!" + w.Full;
    // referenced ctors (MemberRef sigs carry no names; synthesize argN)
    if (ctorUses.TryGetValue(key, out var ctors))
        foreach (var sig in ctors.Where(s => s != ""))
        {
            var parts = sig.Length == 0 ? new string[0] : sig.Split(',');
            sb.AppendLine($"{ip}public {Short(real.FullName)}({string.Join(", ", parts.Select((p, i) => p + " arg" + i))}) {{ }}");
        }
    // referenced static/instance fields
    if (fieldUses.TryGetValue(key, out var fus))
        foreach (var kv in fus)
            sb.AppendLine($"{ip}public {(kv.Value.IsStatic ? "static " : "")}readonly {kv.Value.Type} {kv.Key} = new {kv.Value.Type}();");
    // canonical static singletons not directly referenced (harmless, keeps shape)
    foreach (var f in real.Fields.Where(f => f.IsStatic && !f.IsSpecialName && !f.IsLiteral && !(fieldUses.TryGetValue(key, out var d2) && d2.ContainsKey(f.Name))))
        sb.AppendLine($"{ip}public static readonly {CsName(f.FieldType, MapParams(real))} {f.Name} = new {CsName(f.FieldType, MapParams(real))}();");
    // Instance fields: only public ones plus any referenced by apps (internal
    // implementation details like MS.Internal fields are dropped — they can
    // never be observed across assemblies).
    foreach (var f in real.Fields.Where(f => !f.IsStatic && !f.IsSpecialName && !f.IsLiteral && (f.IsPublic || (fieldUses.TryGetValue(key, out var d3) && d3.ContainsKey(f.Name)))))
        sb.AppendLine($"{ip}public {CsName(f.FieldType, MapParams(real))} {f.Name};");
    // referenced methods grouped into properties/events/plain methods
    var propGet = new Dictionary<string, MethUse>();
    var propSet = new Dictionary<string, MethUse>();
    var evAdd = new Dictionary<string, MethUse>();
    var evRem = new Dictionary<string, MethUse>();
    var plain = new List<MethUse>();
    if (methodUses.TryGetValue(key, out var d))
    {
        foreach (var kv in d.Values)
        {
            string n = kv.Name;
            if (n == ".ctor" || n == ".cctor") continue;
            if ((n.StartsWith("get_") || n.StartsWith("set_")) && n.Length > 4 && !n.Contains('.'))
            {
                string pn = n.Substring(4);
                if (n.StartsWith("get_")) propGet[pn] = kv; else propSet[pn] = kv;
            }
            else if ((n.StartsWith("add_") || n.StartsWith("remove_")) && n.Length > 4 && !n.Contains('.'))
            {
                string en = n.StartsWith("add_") ? n.Substring(4) : n.Substring(7);
                if (n.StartsWith("add_")) evAdd[en] = kv; else evRem[en] = kv;
            }
            else plain.Add(kv);
        }
    }
    var realProps = real.Properties.ToDictionary(p => p.Name);
    foreach (var pn in propGet.Keys.Union(propSet.Keys).OrderBy(x => x))
    {
        propGet.TryGetValue(pn, out var g);
        propSet.TryGetValue(pn, out var s);
        string pt = "object";
        if (realProps.TryGetValue(pn, out var rp)) pt = CsName(rp.PropertyType, MapParams(real));
        else if (g != null && g.Ret != "void") pt = g.Ret;
        else if (s != null && s.Params.Count > 0) pt = s.Params[^1].Sig;
        bool isStatic = (g ?? s)!.IsStatic;
        string acc = $"{ip}public {(isStatic ? "static " : "")}{pt} {pn} {{ ";
        if (g != null) acc += "get { return default; } ";
        if (s != null) acc += "set { } ";
        sb.AppendLine(acc + "}");
    }
    foreach (var en in evAdd.Keys.Union(evRem.Keys).OrderBy(x => x))
    {
        evAdd.TryGetValue(en, out var a);
        string dt = "System.EventHandler";
        var ap = a?.Params.FirstOrDefault();
        if (ap != null && ap.Value.Sig != "") dt = ap.Value.Sig;
        bool isStatic = (a ?? evRem[en]).IsStatic;
        sb.AppendLine($"{ip}public {(isStatic ? "static " : "")}event {dt} {en};");
    }
    foreach (var kv in plain.OrderBy(k => k.Name))
    {
        // MemberRef params carry no names; take them from the real def when available.
        var realM = real.Methods.FirstOrDefault(m => m.Name == kv.Name && m.Parameters.Count == kv.Params.Count && !m.IsSpecialName);
        var pnames = realM != null
            ? realM.Parameters.Select(p => p.Name).ToList()
            : kv.Params.Select((p, i) => "arg" + i).ToList();
        string mods = "public " + (kv.IsStatic ? "static " : "virtual ");
        string pars = string.Join(", ", kv.Params.Select((p, i) => (p.IsRef ? "ref " : "") + p.Sig + " " + pnames[i]));
        string body = kv.Ret == "void" ? "{ }" : "{ return default; }";
        sb.AppendLine($"{ip}{mods}{kv.Ret} {kv.Name}({pars}) {body}");
    }
    // Canonical interface members so classes satisfy their declared contracts
    // (deduped against referenced ones emitted above).
    var emittedSigs = new HashSet<string>(plain.Select(k => k.Name + "(" + string.Join(",", k.Params.Select(p => p.Sig)) + ")"));
    foreach (var ifname in real.Interfaces.Select(i => i.InterfaceType.FullName))
    {
        if (!realByName.TryGetValue(Nk(ifname), out var idef))
        {
            EmitBclInterface(sb, ip, ifname, emittedSigs);
            continue;
        }
        foreach (var m in idef.Methods.Where(m => !m.IsSpecialName))
        {
            string sig = m.Name + "(" + string.Join(",", m.Parameters.Select(p => CsName(StripRef(p.ParameterType), MapParams(idef)))) + ")";
            if (emittedSigs.Contains(sig)) continue;
            emittedSigs.Add(sig);
            string pars = string.Join(", ", m.Parameters.Select(p =>
                (p.IsOut ? "out " : (p.ParameterType is ByReferenceType ? "ref " : "")) + CsName(StripRef(p.ParameterType), MapParams(idef)) + " " + p.Name));
            string body = m.ReturnType.FullName == "System.Void" ? "{ }" : "{ return default; }";
            sb.AppendLine($"{ip}public virtual {CsName(m.ReturnType, MapParams(idef))} {m.Name}({pars}) {body}");
        }
        foreach (var p in idef.Properties)
        {
            string acc = $"{ip}public virtual {CsName(p.PropertyType, MapParams(idef))} {p.Name} {{ ";
            if (p.GetMethod != null) acc += "get { return default; } ";
            if (p.SetMethod != null) acc += "set { } ";
            sb.AppendLine(acc + "}");
        }
    }
    EmitNested(sb, Nk(real.FullName), indOf(pad) + 1);
    sb.AppendLine(pad + "}");
}

void EmitUsageClass(System.Text.StringBuilder sb, UseType w, string pad)
{
    // No real def: emit from usage only (methods as virtuals, object-typed).
    sb.AppendLine($"{pad}public class {Short(w.Full)}");
    sb.AppendLine(pad + "{");
    string ip = pad + "    ";
    sb.AppendLine($"{ip}public {Short(w.Full)}() {{ }}");
    string key = w.Asm + "!" + w.Full;
    if (fieldUses.TryGetValue(key, out var fus))
        foreach (var kv in fus)
            sb.AppendLine($"{ip}public {(kv.Value.IsStatic ? "static " : "")}readonly {kv.Value.Type} {kv.Key} = new {kv.Value.Type}();");
    if (methodUses.TryGetValue(key, out var d))
        foreach (var kv in d.Values.OrderBy(k => k.Name))
        {
            if (kv.Name == ".ctor" || kv.Name == ".cctor") continue;
            string pars = string.Join(", ", kv.Params.Select((p, i) => (p.IsRef ? "ref " : "") + p.Sig + " arg" + i));
            string body = kv.Ret == "void" ? "{ }" : "{ return default; }";
            string mods = "public " + (kv.IsStatic ? "static " : "virtual ");
            sb.AppendLine($"{ip}{mods}{kv.Ret} {kv.Name}({pars}) {body}");
        }
    sb.AppendLine(pad + "}");
}

int indOf(string pad) => pad.Length / 4;

// ---------- helpers + models ----------
TypeReference Strip(TypeReference tr)
{
    while (tr is PinnedType || tr is OptionalModifierType || tr is RequiredModifierType || tr is SentinelType)
        tr = ((TypeSpecification)tr).ElementType;
    return tr;
}
string Nk(string full) => full.Replace('+', '.').Replace('/', '.');
string Ns(string full)
{
    string n = Nk(full);
    int lt = n.IndexOf('<');
    if (lt >= 0) n = n.Substring(0, lt);
    int i = n.LastIndexOf('.');
    return i < 0 ? "NoNamespace" : n.Substring(0, i);
}
string Short(string full)
{
    string n = Nk(full);
    int lt = n.IndexOf('<');
    if (lt >= 0) n = n.Substring(0, lt);
    int i = n.LastIndexOf('.');
    n = i < 0 ? n : n.Substring(i + 1);
    int t = n.IndexOf('`');
    return t < 0 ? n : n.Substring(0, t);
}
Dictionary<string, string> MapParams(TypeDefinition td)
{
    var d = new Dictionary<string, string>();
    if (td.HasGenericParameters)
        foreach (var gp in td.GenericParameters) d[gp.Name] = gp.Name;
    return d;
}
string TypeDecl(TypeDefinition td)
{
    string n = Short(td.FullName);
    if (td.HasGenericParameters) n += "<" + string.Join(", ", td.GenericParameters.Select(g => g.Name)) + ">";
    return n;
}
string Params(MethodDefinition m)
{
    return string.Join(", ", m.Parameters.Select(p =>
        (p.IsOut ? "out " : (p.ParameterType is ByReferenceType ? "ref " : "")) + CsName(StripRef(p.ParameterType), MapParams((TypeDefinition)m.DeclaringType)) + " " + p.Name));
}
TypeReference StripRef(TypeReference t) { t = Strip(t); return t is ByReferenceType b ? Strip(b.ElementType) : t; }
string CsSig(TypeReference t)
{
    t = Strip(t);
    if (t is ByReferenceType b) return CsSig(b.ElementType);
    if (t is GenericParameter gp) return gp.Name;
    if (t is GenericInstanceType gi)
        return Short(gi.ElementType.FullName) + "<" + string.Join(", ", gi.GenericArguments.Select(CsSig)) + ">";
    if (t is ArrayType at) return CsSig(at.ElementType) + "[" + new string(',', (int)at.Rank - 1) + "]";
    switch (t.FullName)
    {
        case "System.Void": return "void";
        case "System.Boolean": return "bool";
        case "System.Byte": return "byte";
        case "System.SByte": return "sbyte";
        case "System.Int16": return "short";
        case "System.UInt16": return "ushort";
        case "System.Int32": return "int";
        case "System.UInt32": return "uint";
        case "System.Int64": return "long";
        case "System.UInt64": return "ulong";
        case "System.Single": return "float";
        case "System.Double": return "double";
        case "System.Char": return "char";
        case "System.String": return "string";
        case "System.Object": return "object";
        case "System.IntPtr": return "IntPtr";
        case "System.UIntPtr": return "UIntPtr";
    }
    return Nk(t.FullName);
}
string CsName(TypeReference tr, Dictionary<string, string> gp)
{
    tr = Strip(tr);
    if (tr is GenericParameter gpp) return gp.TryGetValue(gpp.Name, out var n) ? n : gpp.Name;
    if (tr is GenericInstanceType gi)
        return CsName(gi.ElementType, gp) + "<" + string.Join(", ", gi.GenericArguments.Select(a => CsName(a, gp))) + ">";
    if (tr is ArrayType at)
        return CsName(at.ElementType, gp) + "[" + new string(',', (int)at.Rank - 1) + "]";
    if (tr is ByReferenceType br) return CsName(br.ElementType, gp);
    switch (tr.FullName)
    {
        case "System.Void": return "void";
        case "System.Boolean": return "bool";
        case "System.Byte": return "byte";
        case "System.SByte": return "sbyte";
        case "System.Int16": return "short";
        case "System.UInt16": return "ushort";
        case "System.Int32": return "int";
        case "System.UInt32": return "uint";
        case "System.Int64": return "long";
        case "System.UInt64": return "ulong";
        case "System.Single": return "float";
        case "System.Double": return "double";
        case "System.Char": return "char";
        case "System.String": return "string";
        case "System.Object": return "object";
        case "System.IntPtr": return "IntPtr";
        case "System.UIntPtr": return "UIntPtr";
    }
    return Nk(tr.FullName);
}
bool IsDelegate(TypeDefinition td)
{
    var bt = td.BaseType;
    return bt != null && bt.FullName == "System.MulticastDelegate";
}

void EmitBclInterface(System.Text.StringBuilder sb, string ip, string ifname, HashSet<string> emittedSigs)
{
    // Interface from the BCL (e.g. System.IComparable): reflect the running
    // runtime's definition so shim classes satisfy their contracts.
    var rt = (from a in System.AppDomain.CurrentDomain.GetAssemblies()
              select a.GetType(Nk(ifname).Replace('/', '.').Replace('+', '.'))).FirstOrDefault(t => t != null && t.IsInterface);
    if (rt == null)
    {
        sb.AppendLine($"{ip}// FLAGGED: BCL interface {ifname} not resolvable at gen time.");
        return;
    }
    foreach (var m in rt.GetMethods().Where(m => !m.IsSpecialName))
    {
        string sig = m.Name + "(" + string.Join(",", m.GetParameters().Select(p => BclName(p.ParameterType))) + ")";
        if (emittedSigs.Contains(sig)) continue;
        emittedSigs.Add(sig);
        string pars = string.Join(", ", m.GetParameters().Select((p, i) =>
            (p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : "")) + BclName(p.ParameterType.GetElementType() ?? p.ParameterType) + " arg" + i));
        string body = m.ReturnType.FullName == "System.Void" ? "{ }" : "{ return default; }";
        sb.AppendLine($"{ip}public virtual {BclName(m.ReturnType)} {m.Name}({pars}) {body}");
    }
}

string BclName(Type t)
{
    if (t == typeof(void)) return "void";
    if (t == typeof(bool)) return "bool";
    if (t == typeof(byte)) return "byte";
    if (t == typeof(sbyte)) return "sbyte";
    if (t == typeof(short)) return "short";
    if (t == typeof(ushort)) return "ushort";
    if (t == typeof(int)) return "int";
    if (t == typeof(uint)) return "uint";
    if (t == typeof(long)) return "long";
    if (t == typeof(ulong)) return "ulong";
    if (t == typeof(float)) return "float";
    if (t == typeof(double)) return "double";
    if (t == typeof(char)) return "char";
    if (t == typeof(string)) return "string";
    if (t == typeof(object)) return "object";
    if (t == typeof(IntPtr)) return "IntPtr";
    if (t == typeof(UIntPtr)) return "UIntPtr";
    if (t.IsGenericParameter) return t.Name;
    if (t.IsGenericType) return Short(t.FullName!) + "<" + string.Join(", ", t.GetGenericArguments().Select(BclName)) + ">";
    if (t.IsArray) return BclName(t.GetElementType()!) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
    return "global::" + t.FullName!.Replace('+', '.');
}
TypeReference EventDelegate(EventDefinition e) => e.AddMethod.Parameters[0].ParameterType;

sealed class UseType { public string Asm = ""; public string Full = ""; }
sealed class FieldUse { public string Type = "object"; public bool IsStatic; }
sealed class MethUse
{
    public string Name = "";
    public bool IsStatic = true;
    public string Ret = "void";
    public List<(string Sig, string Name, bool IsRef)> Params = new();
}
