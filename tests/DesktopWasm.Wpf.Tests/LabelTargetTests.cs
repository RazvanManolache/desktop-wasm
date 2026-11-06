using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>Label.Target: the gallery ListViewPage binds
/// Target="{Binding ElementName=SelectionModeComboBox}"
/// (ListViewPage.xaml line 123). Genuine WPF carries a UIElement Target
/// dependency property used for access-key focus forwarding; the engine
/// Label has no Target at all, so the original page fails to load.
/// All access here is reflection-shaped so the file compiles both
/// before (red) and after (green) the property lands.</summary>
[ResetAmbientState]
public sealed class LabelTargetTests
{
    private static PropertyInfo TargetProperty() =>
        typeof(Label).GetProperty("Target", BindingFlags.Public | BindingFlags.Instance)
        ?? throw new Xunit.Sdk.XunitException("Label exposes no Target property.");

    private static DependencyProperty TargetDp() =>
        (DependencyProperty?)typeof(Label).GetField("TargetProperty", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null)
        ?? throw new Xunit.Sdk.XunitException("Label exposes no TargetProperty dependency property.");

    private static object? GetTarget(Label label) => label.GetValue(TargetDp());

    [Fact]
    public void Target_property_exists_on_label()
    {
        Assert.NotNull(TargetProperty());
        Assert.NotNull(TargetDp());
    }

    [Fact]
    public void Target_defaults_null_and_retains_direct_reference()
    {
        var label = new Label { Content = "Selection mode" };
        Assert.Null(GetTarget(label));

        var target = new ComboBox();
        label.SetValue(TargetDp(), target);
        Assert.Same(target, GetTarget(label));
    }

    [Fact]
    public void XamlLoader_target_elementname_resolves_forward_named_sibling()
    {
        // Original-page shape (ListViewPage.xaml lines 123-131): the Label
        // precedes its ElementName target in document order.
        var loaded = XamlLoader.Load("""
            <UserControl><StackPanel>
              <Label Name="ModeLabel" Content="Selection mode" Target="{Binding ElementName=SelectionModeComboBox}" />
              <ComboBox Name="SelectionModeComboBox"><ComboBoxItem Content="Single" /></ComboBox>
            </StackPanel></UserControl>
            """);

        var root = (UserControl)loaded.Root;
        var label = Assert.IsType<Label>(loaded.Names["ModeLabel"]);
        var combo = Assert.IsType<ComboBox>(loaded.Names["SelectionModeComboBox"]);

        root.Measure(new WpfSize(300, 300));
        root.Arrange(new WpfRect(0, 0, 300, 300));

        Assert.Same(combo, GetTarget(label));
    }
}
