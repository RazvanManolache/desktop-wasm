using DesktopWasm.Packaging;
using Mono.Cecil;

namespace DesktopWasm.Convert;

/// <summary>
/// Explicit adapter seam owned by the merge-packaging lane: joint-policy
/// view over a module awaiting conversion. Additive only: existing
/// <see cref="AssemblyConverter"/> behavior is unchanged; the composed
/// pipeline calls this to surface joint decisions (routing, collisions)
/// before either rewrite pass runs.
/// </summary>
public static class JointPolicyAdapter
{
    public static FrameworkCapabilities DetectCapabilities(ModuleDefinition module) =>
        JointCapabilities.Detect(module);

    /// <summary>
    /// Walks every type position (base, interfaces, fields, signatures,
    /// properties, events) and reports joint-policy collisions and gaps for
    /// references both maps claim or neither owns. Pure-framework (BCL and
    /// engine) references are never gaps.
    /// </summary>
    public static IReadOnlyList<JointGap> CollectJointGaps(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var capabilities = JointCapabilities.Detect(module);
        var gaps = new List<JointGap>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in WalkTypes(module))
        {
            Check(type.BaseType, type.FullName);
            foreach (var iface in type.Interfaces) Check(iface.InterfaceType, type.FullName);
            foreach (var field in type.Fields) Check(field.FieldType, type.FullName);
            foreach (var method in type.Methods)
            {
                Check(method.ReturnType, type.FullName);
                foreach (var parameter in method.Parameters) Check(parameter.ParameterType, type.FullName);
                foreach (var bodyRef in WalkBodyTypeRefs(method)) Check(bodyRef, type.FullName);
            }
            foreach (var property in type.Properties) Check(property.PropertyType, type.FullName);
            foreach (var evt in type.Events) Check(evt.EventType, type.FullName);
        }
        return gaps.Distinct().ToArray();

        void Check(TypeReference? reference, string owner)
        {
            var scope = Unwrap(reference)?.Scope as AssemblyNameReference;
            if (scope is null || !JointMap.OverlappedAssemblies.Contains(scope.Name)) return;
            var fullName = Unwrap(reference)!.FullName;
            var dot = fullName.LastIndexOf('.');
            if (dot <= 0) return;
            var route = JointPrecedence.ResolveType(
                scope.Name, fullName[..dot], fullName[(dot + 1)..], capabilities);
            if (route.Kind is JointTargetKind.Collision or JointTargetKind.Gap)
            {
                var key = route.Kind + "|" + scope.Name + "|" + fullName;
                if (seen.Add(key))
                    gaps.Add(new JointGap(owner, route.Kind == JointTargetKind.Collision
                        ? JointGap.TypeCollision : JointGap.MissingDependency,
                        route.Kind == JointTargetKind.Collision ? route.Reason :
                        $"Overlapped reference '{scope.Name}:{fullName}' has no route: {route.Reason}"));
            }
        }
    }

    /// <summary>
    /// The WPF pass of the composed conversion: retargets WPF-routed
    /// type-reference scopes onto <paramref name="engineScope"/>. Runs after
    /// the WinForms fork pass (<see cref="JointPipeline"/> order); only
    /// references the joint policy routes to the WPF engine move, so real
    /// WinForms controls are never rebound by this pass. Idempotent.
    /// </summary>
    public static int ApplyWpfScopes(ModuleDefinition module, FrameworkCapabilities capabilities, AssemblyNameReference engineScope)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(engineScope);
        var visited = new HashSet<TypeReference>();
        var count = 0;
        foreach (var type in WalkTypes(module))
        {
            Retarget(type.BaseType);
            foreach (var iface in type.Interfaces) Retarget(iface.InterfaceType);
            foreach (var field in type.Fields) Retarget(field.FieldType);
            foreach (var method in type.Methods)
            {
                Retarget(method.ReturnType);
                foreach (var parameter in method.Parameters) Retarget(parameter.ParameterType);
                foreach (var bodyRef in WalkBodyTypeRefs(method)) Retarget(bodyRef);
            }
            foreach (var property in type.Properties) Retarget(property.PropertyType);
            foreach (var evt in type.Events) Retarget(evt.EventType);
        }
        return count;

        void Retarget(TypeReference? reference)
        {
            var unwrapped = Unwrap(reference);
            if (unwrapped is null || !visited.Add(unwrapped)) return;
            if (unwrapped.Scope is not AssemblyNameReference scope) return;
            if (!JointMap.WpfSourceAssemblies.Contains(scope.Name)) return;
            var fullName = unwrapped.FullName;
            var dot = fullName.LastIndexOf('.');
            if (dot <= 0) return;
            var route = JointPrecedence.ResolveType(
                scope.Name, fullName[..dot], fullName[(dot + 1)..], capabilities);
            if (route.Kind == JointTargetKind.WpfEngine)
            {
                unwrapped.Scope = engineScope;
                count++;
            }
        }
    }

    private static IEnumerable<TypeDefinition> WalkTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var type = stack.Pop();
            yield return type;
            foreach (var nested in type.NestedTypes) stack.Push(nested);
        }
    }

    /// <summary>
    /// Method-body type references: instruction type/member operands with
    /// member declaring types plus method return/parameter and field types.
    /// A reference living only in a body (constructed locally, never stored)
    /// routes exactly like a position reference; skipping bodies leaves
    /// convertible scopes dangling in converted bytes.
    /// </summary>
    private static IEnumerable<TypeReference> WalkBodyTypeRefs(MethodDefinition method)
    {
        if (method.Body is null) yield break;
        foreach (var instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case TypeReference operandType:
                    yield return operandType;
                    break;
                case MemberReference member:
                    if (member.DeclaringType is not null) yield return member.DeclaringType;
                    switch (member)
                    {
                        case MethodReference methodRef:
                            yield return methodRef.ReturnType;
                            foreach (var p in methodRef.Parameters) yield return p.ParameterType;
                            break;
                        case FieldReference fieldRef:
                            yield return fieldRef.FieldType;
                            break;
                    }
                    break;
            }
        }
    }

    private static TypeReference? Unwrap(TypeReference? reference)
    {
        while (reference is TypeSpecification specification)
            reference = specification.ElementType;
        return reference;
    }
}
