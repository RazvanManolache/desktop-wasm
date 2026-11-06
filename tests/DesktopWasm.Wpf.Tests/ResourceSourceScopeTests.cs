namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class ResourceSourceScopeTests
{
    [Fact]
    public void AssemblyQualifiedDictionaryResolvesUnqualifiedClrNamespaceInItsOwnAssembly()
    {
        var app = new Application();
        // The entry and the dictionary can contain the same full type name.
        // A clr-namespace without assembly= belongs to the dictionary owner.
        var entry = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("UnrelatedEntry"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var collision = entry.DefineDynamicModule("entry").DefineType(
            typeof(ResourceSourceButton).FullName!, System.Reflection.TypeAttributes.Public, typeof(Button));
        collision.DefineDefaultConstructor(System.Reflection.MethodAttributes.Public);
        collision.CreateType();
        XamlLoader.CustomElementSource = entry;
        const string source = "pack://application:,,,/DesktopWasm.Wpf.Tests;component/Probe.xaml";
        app.RegisterPackResource(source, """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:local="clr-namespace:DesktopWasm.Wpf.Tests">
                <Style x:Key="probe" TargetType="local:ResourceSourceButton" />
            </ResourceDictionary>
            """);
        var dictionary = XamlLoader.LoadResourceDictionary(
            $"<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Source=\"{source}\" />");
        Assert.True(dictionary.TryGetValue("probe", out var value));
        Assert.Equal(typeof(ResourceSourceButton), Assert.IsType<Style>(value).TargetType);
        Assert.Same(entry, XamlLoader.CustomElementSource);
    }
}

public sealed class ResourceSourceButton : Button { }
