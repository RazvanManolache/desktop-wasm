using System.Reflection;

namespace DesktopWasm.Wpf;

/// <summary>
/// Theme-dictionary key (matches System.Windows.ComponentResourceKey):
/// identifies a resource in an external theme assembly by the owning type
/// plus a resource id. Converted theme assemblies (AvalonDock themes, WPF
/// ToolBar style keys) construct these; lookup compares both parts.
/// Live-verified against PresentationFramework 9.0.21: parameterless plus
/// (Type, object) constructors, TypeInTargetAssembly/ResourceId get/set,
/// virtual Assembly get, Equals/GetHashCode/ToString.
/// </summary>
public class ComponentResourceKey : ResourceKey
{
    public ComponentResourceKey()
    {
    }

    public ComponentResourceKey(Type typeInTargetAssembly, object resourceId)
    {
        TypeInTargetAssembly = typeInTargetAssembly;
        ResourceId = resourceId;
    }

    public Type? TypeInTargetAssembly { get; set; }

    /// <summary>Assembly owning the key type (null until the type is set).</summary>
    public virtual Assembly? Assembly => TypeInTargetAssembly?.Assembly;

    public object? ResourceId { get; set; }

    public override bool Equals(object? obj) =>
        obj is ComponentResourceKey other &&
        Equals(other.TypeInTargetAssembly, TypeInTargetAssembly) &&
        Equals(other.ResourceId, ResourceId);

    public override int GetHashCode() => HashCode.Combine(TypeInTargetAssembly, ResourceId);

    public override string ToString() =>
        (TypeInTargetAssembly?.FullName ?? "[null]") + ";" + (ResourceId?.ToString() ?? "[null]");
}
