using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Mono.Cecil;

namespace DesktopWasm.Convert;

/// <summary>One unmappable reference left after rewriting (fails conversion).</summary>
public sealed record ConvertGap(string Kind, string Detail);

/// <summary>Provenance stamped next to every converted binary.</summary>
public sealed record ConvertReceipt(
    string App,
    string InputSha256,
    string MapVersion,
    string? SourceCommit,
    IReadOnlyList<string> EmbeddedXaml,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ConvertedAt,
    string HostFramework,
    IReadOnlyList<string> Adaptations);

/// <summary>
/// Rewrites a real WPF app binary onto the browser engine (shim, not port):
/// the original assembly is only read, never modified. Framework type
/// references (PresentationFramework/WindowsBase/System.Xaml) are remapped
/// in place onto DesktopWasm.Wpf by name; the app's loose .xaml files are
/// embedded as manifest resources for
/// <see cref="Wpf.WpfAppBoot.LoadComponent(object, Uri)"/>. Any reference
/// with no engine counterpart is a gap: conversion fails with the precise
/// list (like the WinForms GapQueue) instead of shipping a half-bound app.
/// </summary>
public static class AssemblyConverter
{
    /// <summary>
    /// Rewrites <paramref name="inputPath"/> into <paramref name="outputDir"/>.
    /// Returns success plus the receipt, or failure plus the gap list.
    /// </summary>
    public static (bool Ok, IReadOnlyList<ConvertGap> Gaps, ConvertReceipt? Receipt) TryConvert(
        string inputPath, string sourceDir, string outputDir, string? sourceCommit = null, bool requireLooseXaml = true)
    {
        var gaps = new List<ConvertGap>();
        if (!File.Exists(inputPath))
            return (false, [new ConvertGap("input", $"App assembly not found: {inputPath}")], null);
        if (!Directory.Exists(sourceDir))
            return (false, [new ConvertGap("input", $"Source directory not found: {sourceDir} (loose .xaml lives here)")], null);

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(inputPath))!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Wpf.Window).Assembly.Location)!);
        // Read-only framework metadata the upstream folder and the
        // converter runtime lack (slice-55: PresentationCore/
        // PresentationFramework enums inside attribute blobs, e.g.
        // ScreenToGif's Localizability/ThemeInfo rows). Nothing ships
        // from these images: references still rewrite onto the engine or
        // prune, and genuinely absent assemblies keep precise gaps.
        resolver.ResolveFailure += (_, reference) => ResolveSharedFramework(reference, resolver);
        using var assembly = AssemblyDefinition.ReadAssembly(inputPath, new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver });
        var module = assembly.MainModule;
        var engineVersion = typeof(Wpf.Window).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        var engineScope = new AssemblyNameReference("DesktopWasm.Wpf", engineVersion);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dead in module.AssemblyReferences
            .Where(r => WpfTypeMap.SourceAssemblies.Contains(r.Name))
            .ToList())
            module.AssemblyReferences.Remove(dead);
        var registeredEngine = module.AssemblyReferences.FirstOrDefault(r => r.Name == engineScope.Name);
        if (registeredEngine is null)
        {
            module.AssemblyReferences.Add(engineScope);
            registeredEngine = engineScope;
        }

        // Rewrite structurally at every use site: base types, interfaces,
        // fields, signatures, bodies (operands, locals, handlers),
        // attributes and overrides. In-place TypeReference mutation proved
        // unreliable (read-back instances detach from their use sites), so
        // every position gets a rewritten reference assigned into it.
        // Gap detection runs on the live graph after stripping, so orphan
        // rows from removed ballast never fail the build.
        foreach (var (_, type) in WalkTypes(module).ToList())
            RewriteTypePositions(module, type, registeredEngine);
        BindFrameworkReferences(module);
        var adaptations = StripDeadUnloadableTypes(module, gaps);
        ValidateLiveGraph(module, gaps, reported);
        FixAssemblyAttributeScopes(module, registeredEngine, adaptations, gaps, reported);
        PruneDeadReferences(module, adaptations);
        if (gaps.Count > 0)
            return (false, gaps, null);

        var embedded = EmbedLooseXaml(module, sourceDir, gaps, requireLooseXaml);
        if (gaps.Count > 0)
            return (false, gaps.Distinct().ToList(), null);

        EmbedAppSettings(module, inputPath, adaptations);

        Directory.CreateDirectory(outputDir);
        var fileName = Path.GetFileName(inputPath);
        var outputPath = Path.Combine(outputDir, fileName);
        module.Write(outputPath, new WriterParameters { WriteSymbols = false });

        var receipt = new ConvertReceipt(
            fileName,
            Sha256Of(inputPath),
            WpfTypeMap.MapVersion,
            sourceCommit,
            embedded,
            WarningsFor(assembly),
            DateTimeOffset.UtcNow,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            adaptations);
        File.WriteAllText(
            Path.Combine(outputDir, Path.GetFileNameWithoutExtension(fileName) + ".receipt.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        return (true, [], receipt);
    }

    /// <summary>Rewrites every type position of one type definition.</summary>
    private static void RewriteTypePositions(ModuleDefinition module, TypeDefinition type, AssemblyNameReference engineScope)
    {
        if (type.BaseType is not null)
            type.BaseType = RewriteType(module, type.BaseType, engineScope);
        foreach (var iface in type.Interfaces)
            iface.InterfaceType = RewriteType(module, iface.InterfaceType, engineScope);
        foreach (var field in type.Fields)
            field.FieldType = RewriteType(module, field.FieldType, engineScope);
        // Property and event signatures are their own metadata blobs (not
        // the accessors'): leaving them behind keeps framework-scoped rows
        // that fail at runtime with TypeLoad (converted OxyPlot
        // DefaultTrackerTemplate) once the framework assembly is pruned.
        foreach (var prop in type.Properties)
            prop.PropertyType = RewriteType(module, prop.PropertyType, engineScope);
        foreach (var ev in type.Events)
            ev.EventType = RewriteType(module, ev.EventType, engineScope);
        // Generic parameter constraints load with their type/method (a stray
        // FrameworkElement constraint broke converted CanvasRenderContext at
        // first instantiation): rewrite them like every other position.
        foreach (var gp in type.GenericParameters)
            RewriteConstraints(module, gp, engineScope);
        foreach (var method in type.Methods)
        {
            method.ReturnType = RewriteType(module, method.ReturnType, engineScope);
            foreach (var p in method.Parameters)
                p.ParameterType = RewriteType(module, p.ParameterType, engineScope);
            foreach (var gp in method.GenericParameters)
                RewriteConstraints(module, gp, engineScope);
            foreach (var over in method.Overrides)
                RewriteSignature(module, over, engineScope);
            if (method.HasBody)
            {
                var processor = method.Body.GetILProcessor();
                foreach (var instr in method.Body.Instructions.ToList())
                {
                    if (instr.Operand is TypeReference typeOperand)
                        instr.Operand = RewriteType(module, typeOperand, engineScope);
                    else if (instr.Operand is MethodReference called)
                    {
                        RewriteSignature(module, called, engineScope);
                        var retargeted = RetargetFrameworkOnlyMember(module, called);
                        if (retargeted is not null)
                        {
                            // Widening long->double is explicit in IL: convert
                            // the argument (exact for UI-scale intervals)
                            // before the rebound call.
                            processor.InsertBefore(instr, processor.Create(Mono.Cecil.Cil.OpCodes.Conv_R8));
                            instr.Operand = retargeted;
                        }
                    }
                    else if (instr.Operand is MemberReference member &&
                             member.DeclaringType is not null)
                    {
                        member.DeclaringType = RewriteType(module, member.DeclaringType, engineScope);
                        // Field references carry their own signature type
                        // (ldsfld Control.BackgroundProperty resolves by name
                        // AND field type): a real-WPF signature type left
                        // behind fails at JIT time with MissingField even
                        // when the declaring type rewrote correctly.
                        if (member is Mono.Cecil.FieldReference fieldOperand)
                            fieldOperand.FieldType = RewriteType(module, fieldOperand.FieldType, engineScope);
                    }
                }
                foreach (var variable in method.Body.Variables)
                    variable.VariableType = RewriteType(module, variable.VariableType, engineScope);
                foreach (var handler in method.Body.ExceptionHandlers)
                {
                    if (handler.CatchType is not null)
                        handler.CatchType = RewriteType(module, handler.CatchType, engineScope);
                }
            }
        }
        // Attribute types stay put (converted apps only carry System
        // attributes); constructors still rebind below, while typeof()
        // arguments (TemplatePart Type=typeof(Grid)) rewrite onto the engine
        // like every other framework position, or they dangle the same way.
        foreach (var attr in type.CustomAttributes)
        {
            if (attr.Constructor is not null)
                RewriteSignature(module, attr.Constructor, engineScope);
            RewriteAttributeArguments(module, attr, engineScope);
        }
        foreach (var method in type.Methods)
            foreach (var attr in method.CustomAttributes)
            {
                if (attr.Constructor is not null)
                    RewriteSignature(module, attr.Constructor, engineScope);
                RewriteAttributeArguments(module, attr, engineScope);
            }
        foreach (var prop in type.Properties)
            foreach (var attr in prop.CustomAttributes)
            {
                if (attr.Constructor is not null)
                    RewriteSignature(module, attr.Constructor, engineScope);
                RewriteAttributeArguments(module, attr, engineScope);
            }
    }

    /// <summary>
    /// Rewrites one generic parameter's constraints (class/new() rides the
    /// flags; type constraints rewrite onto the engine).
    /// </summary>
    private static void RewriteConstraints(ModuleDefinition module, Mono.Cecil.GenericParameter gp, AssemblyNameReference engineScope)
    {
        for (var i = 0; i < gp.Constraints.Count; i++)
            gp.Constraints[i] = new Mono.Cecil.GenericParameterConstraint(
                RewriteType(module, gp.Constraints[i].ConstraintType, engineScope));
    }

    /// <summary>
    /// Rewrites typeof() values inside a custom attribute (constructor and
    /// named arguments, recursing into arrays), so framework types named by
    /// attributes resolve onto the engine after pruning.
    /// </summary>
    private static void RewriteAttributeArguments(ModuleDefinition module, Mono.Cecil.CustomAttribute attr, AssemblyNameReference engineScope)
    {
        for (var i = 0; i < attr.ConstructorArguments.Count; i++)
        {
            var rewritten = RewriteAttributeValue(module, attr.ConstructorArguments[i].Value, engineScope);
            if (!ReferenceEquals(rewritten, attr.ConstructorArguments[i].Value))
                attr.ConstructorArguments[i] = new Mono.Cecil.CustomAttributeArgument(attr.ConstructorArguments[i].Type, rewritten);
        }
        for (var i = 0; i < attr.Properties.Count; i++)
        {
            var named = attr.Properties[i];
            var rewritten = RewriteAttributeValue(module, named.Argument.Value, engineScope);
            if (!ReferenceEquals(rewritten, named.Argument.Value))
                attr.Properties[i] = new Mono.Cecil.CustomAttributeNamedArgument(named.Name,
                    new Mono.Cecil.CustomAttributeArgument(named.Argument.Type, rewritten));
        }
    }

    private static object? RewriteAttributeValue(ModuleDefinition module, object? value, AssemblyNameReference engineScope) =>
        value switch
        {
            TypeReference type => RewriteType(module, type, engineScope),
            Mono.Cecil.CustomAttributeArgument[] array => array
                .Select(a => new Mono.Cecil.CustomAttributeArgument(a.Type, RewriteAttributeValue(module, a.Value, engineScope)))
                .ToArray(),
            _ => value,
        };

    /// <summary>
    /// Rewrites a method reference's full identity: the declaring type plus
    /// every signature position (return and parameter types). Declaring-type
    /// rewrites alone leave real-WPF parameter types behind, which the
    /// runtime reports as MissingMethod on first virtual dispatch or
    /// base call (seen with OnPreviewGotKeyboardFocus overrides).
    /// Idempotent: engine, framework and local positions rewrite to
    /// themselves, so shared definitions reached through call operands
    /// are never corrupted.
    /// </summary>
    private static void RewriteSignature(ModuleDefinition module, MethodReference method, AssemblyNameReference engineScope)
    {
        if (method is GenericInstanceMethod generic)
        {
            if (generic.ElementMethod is not null)
                RewriteSignature(module, generic.ElementMethod, engineScope);
            for (var i = 0; i < generic.GenericArguments.Count; i++)
                generic.GenericArguments[i] = RewriteType(module, generic.GenericArguments[i], engineScope);
            return;
        }
        if (method.DeclaringType is not null)
            method.DeclaringType = RewriteType(module, method.DeclaringType, engineScope);
        method.ReturnType = RewriteType(module, method.ReturnType, engineScope);
        foreach (var p in method.Parameters)
            p.ParameterType = RewriteType(module, p.ParameterType, engineScope);
    }

    /// <summary>
    /// Rebinds BCL members the compile-time framework has but the net9
    /// browser runtime lacks (the gallery targets net10, where
    /// TimeSpan.FromMilliseconds(long) is new, while BrowserHost runs net9).
    /// The long overload rebinds to the widening double overload, exact for
    /// UI-scale intervals. Table-driven by shape so the next
    /// framework-version-only member is one more row, not a new mechanism.
    /// </summary>
    private static MethodReference? RetargetFrameworkOnlyMember(ModuleDefinition module, MethodReference called)
    {
        if (called.Name != "FromMilliseconds" || called.Parameters.Count != 1
            || called.DeclaringType?.FullName != "System.TimeSpan"
            || called.Parameters[0].ParameterType.FullName != "System.Int64")
            return null;
        var rebound = new MethodReference(called.Name, called.ReturnType, called.DeclaringType)
        {
            HasThis = called.HasThis,
        };
        rebound.Parameters.Add(new ParameterDefinition(module.TypeSystem.Double));
        return rebound;
    }

    /// <summary>Returns the rewritten reference (new object when remapped).</summary>
    private static TypeReference RewriteType(ModuleDefinition module, TypeReference type, AssemblyNameReference engineScope)
    {
        switch (type)
        {
            case GenericInstanceType generic:
            {
                var rewritten = new GenericInstanceType(
                    RewriteType(module, generic.ElementType, engineScope));
                foreach (var arg in generic.GenericArguments)
                    rewritten.GenericArguments.Add(RewriteType(module, arg, engineScope));
                return rewritten;
            }
            case ArrayType array:
                return new ArrayType(RewriteType(module, array.ElementType, engineScope), array.Rank);
            case ByReferenceType byRef:
                return new ByReferenceType(RewriteType(module, byRef.ElementType, engineScope));
            case PointerType pointer:
                return new PointerType(RewriteType(module, pointer.ElementType, engineScope));
            case RequiredModifierType required:
                return new RequiredModifierType(
                    RewriteType(module, required.ModifierType, engineScope),
                    RewriteType(module, required.ElementType, engineScope));
            case OptionalModifierType optional:
                return new OptionalModifierType(
                    RewriteType(module, optional.ModifierType, engineScope),
                    RewriteType(module, optional.ElementType, engineScope));
            case PinnedType pinned:
                return new PinnedType(RewriteType(module, pinned.ElementType, engineScope));
            case GenericParameter _:
            case FunctionPointerType _:
                return type;
        }
        // Nested framework types map allowlist-first (declaring type through
        // the map) with an engine RuntimeType fallback for the nested name
        // (collection Enumerators); anything else passes through untouched.
        if (type.IsNested)
            return RewriteNestedType(module, type, engineScope) ?? type;
        // MapType decides (source scopes plus scope-agnostic redirects like
        // ICommand); unmapped references of any scope pass through untouched.
        if (type.Scope is not AssemblyNameReference scope)
            return type;
        var mapped = WpfTypeMap.MapType(scope.Name, type.Namespace, type.Name);
        if (mapped is null) return type;
        // Enum/struct identity is part of an IL signature, including return
        // types such as MessageBoxResult. Losing it produces MissingMethod
        // even when the mapped method exists with the same apparent types.
        return new TypeReference(mapped.Value.Namespace, mapped.Value.Name, module, engineScope, type.IsValueType);
    }

    /// <summary>
    /// Rewrites a nested framework reference onto the engine when the
    /// declaring chain maps and the nested path resolves in the engine
    /// runtime (see WpfTypeMap.MapNestedType). Returns null otherwise.
    /// </summary>
    private static TypeReference? RewriteNestedType(ModuleDefinition module, TypeReference type, AssemblyNameReference engineScope)
    {
        var nested = new List<string>();
        var current = type;
        while (current is not null && current.IsNested)
        {
            if (current is TypeSpecification) return null;
            nested.Add(current.Name);
            current = current.DeclaringType;
        }
        if (current is null || current is TypeSpecification) return null;
        if (current.Scope is not AssemblyNameReference scope) return null;
        nested.Reverse();
        var engineType = WpfTypeMap.MapNestedType(scope.Name, current.Namespace, current.Name, nested);
        if (engineType is null) return null;
        return ToEngineReference(module, engineScope, engineType);
    }

    private static TypeReference ToEngineReference(ModuleDefinition module, AssemblyNameReference engineScope, Type engineType)
    {
        if (engineType.DeclaringType is not null)
        {
            var parent = ToEngineReference(module, engineScope, engineType.DeclaringType);
            var self = new TypeReference(string.Empty, engineType.Name, module, engineScope, engineType.IsValueType);
            self.DeclaringType = parent;
            return self;
        }
        return new TypeReference(engineType.Namespace ?? string.Empty, engineType.Name, module, engineScope, engineType.IsValueType);
    }

    /// <summary>
    /// Whether a nested framework reference maps onto the engine (same
    /// allowlist-then-RuntimeType rule as the rewrite above).
    /// </summary>
    private static bool NestedTypeMaps(TypeReference type)
    {
        var nested = new List<string>();
        var current = type;
        while (current is not null && current.IsNested)
        {
            if (current is TypeSpecification) return false;
            nested.Add(current.Name);
            current = current.DeclaringType;
        }
        if (current is null || current is TypeSpecification) return false;
        if (current.Scope is not AssemblyNameReference scope) return false;
        nested.Reverse();
        return WpfTypeMap.MapNestedType(scope.Name, current.Namespace, current.Name, nested) is not null;
    }

    /// <summary>
    /// Fails loudly on framework references the live graph still needs:
    /// every live TypeReference position (bases, interfaces, fields,
    /// signatures, bodies, attributes, overrides) whose scope is a WPF
    /// assembly must map onto the engine. Orphan rows from stripped types
    /// never appear here because only live positions are walked.
    /// </summary>
    private static void ValidateLiveGraph(ModuleDefinition module, List<ConvertGap> gaps, HashSet<string> reported)
    {
        foreach (var typeRef in CollectLiveTypeRefs(module))
        {
            var current = typeRef;
            while (current is TypeSpecification spec)
                current = spec.ElementType;
            if (current is null || current is GenericParameter) continue;
            // Framework names under a non-framework, non-engine scope are
            // stale rows from an incomplete rewrite (never a legitimate
            // reference): after the property/event/constraint rewrite none
            // remain; flagging them fails the build instead of shipping an
            // image whose metadata only breaks at first touch (converted
            // OxyPlot property blobs and generic constraints).
            if (current.Scope is AssemblyNameReference other &&
                current.Namespace.StartsWith("System.Windows.", StringComparison.Ordinal) &&
                !WpfTypeMap.SourceAssemblies.Contains(other.Name) &&
                !string.Equals(other.Name, "DesktopWasm.Wpf", StringComparison.Ordinal))
            {
                AddGap(gaps, reported, "stale-framework-reference", $"{other.Name}:{current.Namespace}.{current.Name}");
                continue;
            }
            if (current.Scope is not AssemblyNameReference scope ||
                !WpfTypeMap.SourceAssemblies.Contains(scope.Name))
                continue;
            if (current.IsNested)
            {
                // Mapped nested types (collection Enumerators) rewrite onto
                // the engine above; only the unmapped remainder gaps here.
                if (NestedTypeMaps(current)) continue;
                AddGap(gaps, reported, "nested-type", $"{current.FullName} (nested framework types are not mapped)");
                continue;
            }
            if (WpfTypeMap.MapType(scope.Name, current.Namespace, current.Name) is null)
                AddGap(gaps, reported, "unmapped-type", $"{scope.Name}:{current.Namespace}.{current.Name}");
        }
    }

    /// <summary>
    /// Assembly rows nothing in the browser can ever resolve: the engine
    /// never reads assembly attributes by any API (no GetCustomAttributes
    /// or CustomAttributeData use anywhere in src), and converted code
    /// cannot name these types either (any live reference gaps). Keeping
    /// such a row only preserves a dangling reference that breaks eager
    /// enumeration in converted code (ResourceManager walks every assembly
    /// attribute). Covered: the XAML compiler's namespace markers
    /// (System.Windows.Markup XmlnsDefinition/XmlnsPrefix/XmlnsCompatibleWith,
    /// home assembly System.Xaml), the loose-content marker
    /// (System.Windows.Resources AssemblyAssociatedContentFileAttribute,
    /// home PresentationCore; the engine has no content-stream surface
    /// reading it), and the theme-location marker (System.Windows
    /// ThemeInfoAttribute, home PresentationFramework; the engine locates
    /// converted default styles by convention, never this row).
    /// </summary>
    private static bool AssemblyAttributeIsInert(string ns, string name) =>
        (ns.Equals("System.Windows.Markup", StringComparison.Ordinal) &&
            (name.Equals("XmlnsDefinitionAttribute", StringComparison.Ordinal) ||
                name.Equals("XmlnsPrefixAttribute", StringComparison.Ordinal) ||
                name.Equals("XmlnsCompatibleWithAttribute", StringComparison.Ordinal))) ||
        (ns.Equals("System.Windows.Resources", StringComparison.Ordinal) &&
            name.Equals("AssemblyAssociatedContentFileAttribute", StringComparison.Ordinal)) ||
        (ns.Equals("System.Windows", StringComparison.Ordinal) &&
            name.Equals("ThemeInfoAttribute", StringComparison.Ordinal));

    /// <summary>
    /// Repairs assembly and module custom attributes before the writer runs:
    /// type-level attributes rewrite onto the engine (or gap) through the
    /// live-graph pass, but assembly/module attributes are never rewritten,
    /// so an attribute type scoped to a pruned convertible assembly (the
    /// System.Xaml XmlnsDefinition/XmlnsPrefix rows every XAML library
    /// carries) would ship with its scope silently re-pointed by the writer
    /// onto an unrelated live reference (observed: System.Collections),
    /// breaking at first touch (converted HandyControl's Lang resource
    /// lookup enumerates assembly attributes and throws on the dangling
    /// row). Mapped attribute types remap onto the engine like every other
    /// position; known-inert XAML namespace markers are dropped (the engine
    /// resolves converted types through its own element source, never these
    /// rows); anything else under a convertible source scope is a precise
    /// gap so conversion fails loudly instead of shipping a mis-scoped row.
    /// </summary>
    private static void FixAssemblyAttributeScopes(ModuleDefinition module, AssemblyNameReference engineScope,
        List<string> adaptations, List<ConvertGap> gaps, HashSet<string> reported)
    {
        foreach (var owner in new[] { module.Assembly.CustomAttributes, module.CustomAttributes })
        {
            foreach (var attr in owner.ToList())
            {
                var attributeType = attr.AttributeType;
                while (attributeType is TypeSpecification spec)
                    attributeType = spec.ElementType;
                if (attributeType is null || attributeType.Scope is not AssemblyNameReference scope)
                    continue;
                if (!WpfTypeMap.SourceAssemblies.Contains(scope.Name))
                    continue;
                var mapped = WpfTypeMap.MapType(scope.Name, attributeType.Namespace, attributeType.Name);
                if (mapped is not null)
                {
                    // AttributeType is read-only, so the attribute is rebuilt
                    // around the rewritten constructor (arguments re-pointed
                    // like the type-level attribute path).
                    var index = owner.IndexOf(attr);
                    owner[index] = RemapAssemblyAttribute(module, attr, mapped.Value, engineScope);
                    adaptations.Add($"Remapped assembly attribute '{scope.Name}:{attributeType.FullName}' onto the engine.");
                    continue;
                }
                if (AssemblyAttributeIsInert(attributeType.Namespace, attributeType.Name))
                {
                    owner.Remove(attr);
                    adaptations.Add($"Removed assembly attribute '{attributeType.FullName}' (XAML namespace marker; the engine resolves converted types without it).");
                    continue;
                }
                AddGap(gaps, reported, "unmapped-assembly-attribute",
                    $"{scope.Name}:{attributeType.Namespace}.{attributeType.Name}");
            }
        }
    }

    /// <summary>
    /// Rebuilds one assembly/module attribute around an engine-scoped
    /// constructor (attribute ctor signatures cannot be generic, so only
    /// the declaring type, return type and parameter types rewrite).
    /// </summary>
    private static CustomAttribute RemapAssemblyAttribute(ModuleDefinition module, CustomAttribute attr,
        (string Assembly, string Namespace, string Name) mapped, AssemblyNameReference engineScope)
    {
        MethodReference ctor;
        if (attr.Constructor is null)
        {
            var attributeType = new TypeReference(mapped.Namespace, mapped.Name, module, engineScope, false);
            ctor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
        }
        else
        {
            var source = attr.Constructor;
            ctor = new MethodReference(source.Name,
                RewriteType(module, source.ReturnType, engineScope),
                RewriteType(module, source.DeclaringType, engineScope))
            {
                HasThis = source.HasThis,
                ExplicitThis = source.ExplicitThis,
            };
            foreach (var p in source.Parameters)
                ctor.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes,
                    RewriteType(module, p.ParameterType, engineScope)));
        }
        var replacement = new CustomAttribute(ctor);
        foreach (var arg in attr.ConstructorArguments)
            replacement.ConstructorArguments.Add(new CustomAttributeArgument(
                RewriteType(module, arg.Type, engineScope),
                RewriteAttributeValue(module, arg.Value, engineScope)));
        foreach (var field in attr.Fields)
            replacement.Fields.Add(new Mono.Cecil.CustomAttributeNamedArgument(field.Name,
                new CustomAttributeArgument(
                    RewriteType(module, field.Argument.Type, engineScope),
                    RewriteAttributeValue(module, field.Argument.Value, engineScope))));
        foreach (var prop in attr.Properties)
            replacement.Properties.Add(new Mono.Cecil.CustomAttributeNamedArgument(prop.Name,
                new CustomAttributeArgument(
                    RewriteType(module, prop.Argument.Type, engineScope),
                    RewriteAttributeValue(module, prop.Argument.Value, engineScope))));
        return replacement;
    }

    /// <summary>Every TypeReference occurrence reachable from live code.</summary>
    private static HashSet<TypeReference> CollectLiveTypeRefs(ModuleDefinition module)
    {
        var found = new HashSet<TypeReference>();
        void Add(TypeReference? reference)
        {
            if (reference is null) return;
            found.Add(reference);
            if (reference is GenericInstanceType generic)
                foreach (var arg in generic.GenericArguments)
                    Add(arg);
        }
        void AddSignature(MethodReference? reference)
        {
            if (reference is null) return;
            if (reference is GenericInstanceMethod generic)
            {
                AddSignature(generic.ElementMethod);
                foreach (var arg in generic.GenericArguments)
                    Add(arg);
                return;
            }
            Add(reference.DeclaringType);
            Add(reference.ReturnType);
            foreach (var p in reference.Parameters)
                Add(p.ParameterType);
        }
        foreach (var (_, type) in WalkTypes(module))
        {
            Add(type.BaseType);
            foreach (var iface in type.Interfaces)
                Add(iface.InterfaceType);
            foreach (var field in type.Fields)
                Add(field.FieldType);
            // Property, event and constraint blobs resolve at runtime (type
            // load validates generic constraints; reflection reads the
            // rest): they are live positions, or pruning orphans them the
            // same way method bodies were orphaned before.
            foreach (var prop in type.Properties)
                Add(prop.PropertyType);
            foreach (var ev in type.Events)
                Add(ev.EventType);
            foreach (var gp in type.GenericParameters)
                foreach (var constraint in gp.Constraints)
                    Add(constraint.ConstraintType);
            foreach (var method in type.Methods)
            {
                Add(method.ReturnType);
                foreach (var p in method.Parameters)
                    Add(p.ParameterType);
                foreach (var gp in method.GenericParameters)
                    foreach (var constraint in gp.Constraints)
                        Add(constraint.ConstraintType);
                foreach (var over in method.Overrides)
                    AddSignature(over);
                if (method.HasBody)
                {
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is MethodReference called)
                        {
                            AddSignature(called);
                        }
                        else if (instr.Operand is MemberReference member)
                        {
                            Add(member.DeclaringType);
                            Add(member as TypeReference);
                            // Field references carry their own signature type:
                            // an unmapped field type must gap like any other
                            // unmapped type instead of slipping into the image.
                            if (member is FieldReference fieldMember)
                                Add(fieldMember.FieldType);
                        }
                        else if (instr.Operand is TypeReference typeRef)
                        {
                            Add(typeRef);
                        }
                    }
                }
            }
            foreach (var attr in type.CustomAttributes)
            {
                Add(attr.AttributeType);
                AddSignature(attr.Constructor);
            }
        }
        return found;
    }

    /// <summary>
    /// Binds pass-through framework references (System.Runtime, etc.) to the
    /// host runtime generation: a net10.0-built app records 10.0.0.0 contract
    /// versions the net9.0 browser/desktop runtime refuses to load. Rewrites
    /// only references that resolve in the converter's own (same-generation)
    /// runtime; NuGet packages that are not part of the shared framework are
    /// left untouched and fail loudly at runtime only if actually touched.
    /// </summary>
    private static void BindFrameworkReferences(ModuleDefinition module)
    {
        foreach (var reference in module.AssemblyReferences)
        {
            if (WpfTypeMap.SourceAssemblies.Contains(reference.Name)) continue;
            if (reference.Name == "DesktopWasm.Wpf") continue;
            Assembly running;
            try
            {
                running = Assembly.Load(new AssemblyName(reference.Name));
            }
            catch
            {
                continue;
            }
            var name = running.GetName();
            if (name.Version is null) continue;
            reference.Version = name.Version;
            if (name.CultureName is not null) reference.Culture = name.CultureName;
            var token = name.GetPublicKeyToken();
            if (token is not null && token.Length > 0) reference.PublicKeyToken = token;
        }
    }

    /// <summary>
    /// Resolves a framework assembly from the installed shared frameworks
    /// for metadata reading only (see the ResolveFailure hook in
    /// TryConvert). Prefers the reference's own major.minor, else the
    /// highest installed version. Returns null where no shared framework
    /// is installed, so the precise rewrite-error gap stands.
    /// </summary>
    private static AssemblyDefinition? ResolveSharedFramework(AssemblyNameReference reference, DefaultAssemblyResolver resolver)
    {
        try
        {
            var runtimeFile = typeof(object).Assembly.Location;
            var shared = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeFile)!));
            if (shared is null) return null;
            string? best = null;
            var bestVersion = new Version(0, 0, 0, 0);
            var wanted = reference.Version;
            foreach (var pack in new[] { "Microsoft.WindowsDesktop.App", "Microsoft.NETCore.App" })
            {
                var packDir = Path.Combine(shared, pack);
                if (!Directory.Exists(packDir)) continue;
                foreach (var versionDir in Directory.GetDirectories(packDir))
                {
                    var candidate = Path.Combine(versionDir, reference.Name + ".dll");
                    if (!File.Exists(candidate)) continue;
                    if (!Version.TryParse(Path.GetFileName(versionDir), out var version)) continue;
                    if (wanted is not null && version.Major == wanted.Major && version.Minor == wanted.Minor)
                        return AssemblyDefinition.ReadAssembly(candidate,
                            new ReaderParameters { AssemblyResolver = resolver });
                    if (version > bestVersion)
                    {
                        best = candidate;
                        bestVersion = version;
                    }
                }
            }
            return best is null ? null : AssemblyDefinition.ReadAssembly(best,
                new ReaderParameters { AssemblyResolver = resolver });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drops designer-dead types whose bases cannot resolve (HelloWorld's
    /// Properties.Settings over the missing ConfigurationManager package):
    /// the browser loader resolves the full closure eagerly, so one dead
    /// designer type would sink the whole app. Only types referenced from
    /// outside themselves are kept; a live type over a missing library is a
    /// gap, never a silent strip. Removals are recorded as adaptations.
    /// </summary>
    private static List<string> StripDeadUnloadableTypes(ModuleDefinition module, List<ConvertGap> gaps)
    {
        var adaptations = new List<string>();
        bool changed;
        do
        {
            changed = false;
            foreach (var (owner, type) in WalkTypes(module).ToList())
            {
                // Generated BAML helpers (XamlGeneratedNamespace) only serve
                // the framework BamlReader, which never runs in the browser.
                if (IsBamlHelper(type))
                {
                    if (IsReferencedFromOutside(type, module))
                    {
                        gaps.Add(new ConvertGap("missing-dependency",
                            $"{type.FullName} derives from BAML infrastructure but is live."));
                        continue;
                    }
                    owner.Remove(type);
                    adaptations.Add($"Removed BAML-helper type '{type.FullName}' (framework BamlReader never runs).");
                    changed = true;
                    continue;
                }
                if (BaseResolves(type)) continue;
                // Framework-scoped bases stay gaps (engine coverage work),
                // never silent strips: only pass-through ballast is removed.
                if (BaseScopeIsFramework(type)) continue;
                if (IsReferencedFromOutside(type, module))
                {
                    gaps.Add(new ConvertGap("missing-dependency",
                        $"{type.FullName} is live but its base '{type.BaseType?.FullName}' cannot resolve."));
                    continue;
                }
                owner.Remove(type);
                adaptations.Add($"Removed dead unloadable type '{type.FullName}'.");
                changed = true;
            }
        } while (changed);
        return adaptations;
    }

    /// <summary>
    /// Prunes references that are neither used by the live type graph nor
    /// resolvable in this (same-generation-as-browser) runtime. Stale table
    /// rows from stripped types do not count as use.
    /// </summary>
    private static void PruneDeadReferences(ModuleDefinition module, List<string> adaptations)
    {
        var live = CollectLiveScopes(module);
        foreach (var reference in module.AssemblyReferences.ToList())
        {
            if (reference.Name == "DesktopWasm.Wpf") continue;
            if (live.Contains(reference.Name)) continue;
            if (ResolvesInRuntime(reference.Name)) continue;
            module.AssemblyReferences.Remove(reference);
            adaptations.Add($"Pruned unused unresolvable reference '{reference.Name}'.");
        }
    }

    private static IEnumerable<(ICollection<TypeDefinition> Owner, TypeDefinition Type)> WalkTypes(ModuleDefinition module)
    {
        var stack = new Stack<(ICollection<TypeDefinition>, TypeDefinition)>(
            module.Types.Select(t => ((ICollection<TypeDefinition>)module.Types, t)));
        while (stack.Count > 0)
        {
            var (owner, type) = stack.Pop();
            yield return (owner, type);
            foreach (var nested in type.NestedTypes)
                stack.Push((type.NestedTypes, nested));
        }
    }

    /// <summary>Whether the base chain touches BAML infrastructure.</summary>
    private static bool IsBamlHelper(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current is TypeSpecification spec)
            current = spec.ElementType;
        while (current is not null)
        {
            if (current is GenericParameter) return false;
            if (current.FullName == "System.Windows.Markup.InternalTypeHelper") return true;
            if (current.Scope is not ModuleDefinition) return false;
            var definition = current.Resolve();
            if (definition is null) return false;
            current = definition.BaseType;
            while (current is TypeSpecification inner)
                current = inner.ElementType;
        }
        return false;
    }

    /// <summary>Whether the unresolvable base lives in a WPF assembly.</summary>
    private static bool BaseScopeIsFramework(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current is TypeSpecification spec)
            current = spec.ElementType;
        return current?.Scope is AssemblyNameReference scope &&
            WpfTypeMap.SourceAssemblies.Contains(scope.Name);
    }

    private static bool BaseResolves(TypeDefinition type)
    {
        var current = type.BaseType;
        while (current is TypeSpecification spec)
            current = spec.ElementType;
        if (current is null || current is GenericParameter) return true;
        if (current.Scope is ModuleDefinition) return true;
        if (current.Scope is AssemblyNameReference assembly)
        {
            // A package dependency is resolved by the package graph, not by
            // loading an upstream Windows assembly into the converter itself.
            // Preserve its types (including XAML-only views) for that pass.
            var sibling = Path.Combine(Path.GetDirectoryName(type.Module.FileName)!, assembly.Name + ".dll");
            return File.Exists(sibling) || RuntimeTypeExists(assembly.Name, current.FullName);
        }
        return true;
    }

    private static bool RuntimeTypeExists(string assemblyName, string fullName)
    {
        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            // Cecil spells nested bases with '/' (Outer/Inner); runtime
            // reflection needs '+' (Outer+Inner). Without this, live types
            // over nested framework bases (KGySoft FlagsEnumConverter's
            // EnumFlagDescriptor over TypeConverter/SimplePropertyDescriptor)
            // look unresolvable even though the base ships in the framework.
            return assembly.GetType(fullName) is not null ||
                assembly.GetType(fullName.Replace('/', '+')) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool ResolvesInRuntime(string assemblyName)
    {
        try
        {
            Assembly.Load(new AssemblyName(assemblyName));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether any live code outside the type itself names it.</summary>
    private static bool IsReferencedFromOutside(TypeDefinition target, ModuleDefinition module)
    {
        foreach (var (owner, type) in WalkTypes(module))
        {
            if (ReferenceEquals(type, target)) continue;
            if (NamesType(type.BaseType, target.FullName)) return true;
            foreach (var iface in type.Interfaces)
                if (NamesType(iface.InterfaceType, target.FullName)) return true;
            foreach (var field in type.Fields)
                if (NamesType(field.FieldType, target.FullName)) return true;
            foreach (var method in type.Methods)
            {
                if (NamesType(method.ReturnType, target.FullName)) return true;
                foreach (var p in method.Parameters)
                    if (NamesType(p.ParameterType, target.FullName)) return true;
                if (method.HasBody)
                {
                    foreach (var instr in method.Body.Instructions)
                    {
                        switch (instr.Operand)
                        {
                            case MemberReference memberRef when NamesType(memberRef.DeclaringType, target.FullName):
                            case MemberReference memberAsType when NamesType(memberAsType as TypeReference, target.FullName):
                                return true;
                            case TypeReference typeRef when NamesType(typeRef, target.FullName):
                                return true;
                        }
                    }
                }
            }
            foreach (var attr in type.CustomAttributes)
            {
                if (NamesType(attr.AttributeType, target.FullName)) return true;
                if (NamesType(attr.Constructor?.DeclaringType, target.FullName)) return true;
            }
        }
        return false;
    }

    private static bool NamesType(TypeReference? reference, string fullName)
    {
        while (reference is TypeSpecification spec)
            reference = spec.ElementType;
        if (reference is null) return false;
        if (reference.FullName == fullName) return true;
        if (reference is GenericInstanceType generic)
            return generic.GenericArguments.Any(a => NamesType(a, fullName));
        return false;
    }

    /// <summary>Assembly scopes still named by the live type graph.</summary>
    private static HashSet<string> CollectLiveScopes(ModuleDefinition module)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(TypeReference? reference)
        {
            while (reference is TypeSpecification spec)
                reference = spec.ElementType;
            if (reference is null) return;
            if (reference.Scope is AssemblyNameReference assembly)
                scopes.Add(assembly.Name);
            if (reference is GenericInstanceType generic)
                foreach (var arg in generic.GenericArguments)
                    Add(arg);
        }
        foreach (var (_, type) in WalkTypes(module))
        {
            Add(type.BaseType);
            foreach (var iface in type.Interfaces)
                Add(iface.InterfaceType);
            foreach (var field in type.Fields)
                Add(field.FieldType);
            foreach (var method in type.Methods)
            {
                Add(method.ReturnType);
                foreach (var p in method.Parameters)
                    Add(p.ParameterType);
                if (method.HasBody)
                {
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is MemberReference member)
                        {
                            Add(member.DeclaringType);
                            Add(member as TypeReference);
                            if (member is FieldReference fieldMember)
                                Add(fieldMember.FieldType);
                        }
                        else if (instr.Operand is TypeReference typeRef)
                        {
                            Add(typeRef);
                        }
                    }
                }
            }
            foreach (var attr in type.CustomAttributes)
            {
                Add(attr.AttributeType);
                Add(attr.Constructor?.DeclaringType);
            }
        }
        return scopes;
    }

    private static void AddGap(List<ConvertGap> gaps, HashSet<string> reported, string kind, string detail)
    {
        if (reported.Add(kind + "|" + detail))
            gaps.Add(new ConvertGap(kind, detail));
    }

    private static List<string> EmbedLooseXaml(ModuleDefinition module, string sourceDir, List<ConvertGap> gaps, bool required)
    {
        var embedded = new List<string>();
        var existing = new HashSet<string>(
            module.Resources.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(sourceDir, "*.xaml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (name.Split('/').Any(p => p is "bin" or "obj" or ".git")) continue;
            if (!existing.Add(name))
            {
                gaps.Add(new ConvertGap("resource-clash", $"Duplicate embedded XAML name '{name}'."));
                continue;
            }
            module.Resources.Add(new EmbeddedResource(
                name, Mono.Cecil.ManifestResourceAttributes.Public, File.ReadAllBytes(file)));
            embedded.Add(name);
        }
        if (required && embedded.Count == 0)
            gaps.Add(new ConvertGap("no-xaml", $"No loose .xaml under '{sourceDir}'."));
        return embedded;
    }

    /// <summary>
    /// Embeds the sibling ".dll.config" &lt;appSettings&gt; block as
    /// "__AppSettings.json" so the engine ConfigurationManager adapter serves
    /// the real upstream values (directive item P3a). The config sits next to
    /// the input binary in upstream build output; absent or empty means no
    /// seed (engine defaults to nulls, like a missing section on desktop).
    /// Recorded as an adaptation with the embedded key names.
    /// </summary>
    private static void EmbedAppSettings(ModuleDefinition module, string inputPath, List<string> adaptations)
    {
        var configPath = inputPath + ".config";
        if (!File.Exists(configPath)) return;
        List<KeyValuePair<string, string?>> pairs;
        try
        {
            pairs = XDocument.Load(configPath).Root?.Element("appSettings")?.Elements("add")
                .Select(e => new KeyValuePair<string, string?>((string?)e.Attribute("key") ?? string.Empty, (string?)e.Attribute("value")))
                .Where(p => p.Key.Length > 0)
                .ToList() ?? new List<KeyValuePair<string, string?>>();
        }
        catch
        {
            return;
        }
        if (pairs.Count == 0) return;
        const string resourceName = "__AppSettings.json";
        if (module.Resources.Any(r => r.Name == resourceName)) return;
        module.Resources.Add(new EmbeddedResource(resourceName, Mono.Cecil.ManifestResourceAttributes.Public,
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pairs))));
        adaptations.Add($"Embedded {pairs.Count} appSettings key(s) ({string.Join(", ", pairs.Select(p => p.Key))}) from '{Path.GetFileName(configPath)}'.");
    }

    private static List<string> WarningsFor(AssemblyDefinition assembly)
    {
        var warnings = new List<string>();
        foreach (var name in assembly.MainModule.GetManifestResourceNames())
        {
            if (name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Original compiled BAML still ships inside '{name}' (kept for fidelity; boot prefers loose XAML).");
        }
        return warnings;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IEnumerable<string> GetManifestResourceNames(this ModuleDefinition module) =>
        module.Resources.Select(r => r.Name);
}
