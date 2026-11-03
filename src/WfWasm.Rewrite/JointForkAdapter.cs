using DesktopWasm.Packaging;
using Mono.Cecil;

namespace WfWasm.Rewrite;

/// <summary>
/// Explicit adapter seam owned by the merge-packaging lane: the WinForms
/// pass of the composed conversion. Consults the joint precedence policy
/// per referenced type and retargets only WinForms-routed scopes onto the
/// real fork assembly. Pure-WPF shim names are left for the WPF pass;
/// collisions are reported, never rewritten. Idempotent: a second run
/// retargets nothing.
/// </summary>
public static class JointForkAdapter
{
    public static bool ShouldRewriteToFork(string sourceAssembly, string fullName, FrameworkCapabilities capabilities)
    {
        var dot = fullName.LastIndexOf('.');
        if (dot <= 0) return false;
        var route = JointPrecedence.ResolveType(
            sourceAssembly, fullName[..dot], fullName[(dot + 1)..], capabilities);
        return route.Kind == JointTargetKind.WinFormsEngine;
    }

    /// <summary>
    /// Retargets WinForms-routed type-reference scopes onto
    /// <paramref name="forkScope"/> (which the caller registers in
    /// <c>module.AssemblyReferences</c>). Returns the number of distinct
    /// type references retargeted.
    /// </summary>
    public static int RewriteToForkScopes(ModuleDefinition module, FrameworkCapabilities capabilities, AssemblyNameReference forkScope)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(forkScope);
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
            if (!JointMap.WinFormsSourceAssemblies.Contains(scope.Name)) return;
            if (ShouldRewriteToFork(scope.Name, unwrapped.FullName, capabilities))
            {
                unwrapped.Scope = forkScope;
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

    private static TypeReference? Unwrap(TypeReference? reference)
    {
        while (reference is TypeSpecification specification)
            reference = specification.ElementType;
        return reference;
    }
}
