namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class ConvertedEnumContractTests
{
    [Fact]
    public void Numeric_constants_in_original_IL_retain_WPF_meanings()
    {
        Assert.Equal(0, (int)BindingMode.TwoWay); Assert.Equal(1, (int)BindingMode.OneWay);
        Assert.Equal(2, (int)BindingMode.OneTime); Assert.Equal(3, (int)BindingMode.OneWayToSource); Assert.Equal(4, (int)BindingMode.Default);
        Assert.Equal(4, (int)MessageBoxButton.YesNo); Assert.Equal(3, (int)MessageBoxButton.YesNoCancel);
        Assert.Equal(6, (int)MessageBoxResult.Yes); Assert.Equal(7, (int)MessageBoxResult.No);
        Assert.Equal(16, (int)MessageBoxImage.Error); Assert.Equal(32, (int)MessageBoxImage.Question);
        Assert.Equal(48, (int)MessageBoxImage.Warning); Assert.Equal(64, (int)MessageBoxImage.Information);
    }

    [Fact]
    public void Parameterless_binding_uses_the_target_property_default()
    {
        var model = new Model { Name = "original" };
        var editor = new TextBox();
        editor.SetBinding("Text", new Binding { Path = "Name", Source = model });
        editor.Text = "edited";
        Assert.Equal("edited", model.Name);
        var readout = new TextBlock();
        readout.SetBinding("Text", new Binding { Path = "Name", Source = model });
        readout.Text = "display only";
        Assert.Equal("edited", model.Name);
    }

    [Fact]
    public void A_dialog_requiring_a_decision_never_fabricates_acknowledgement() =>
        Assert.Throws<NotSupportedException>(() => MessageBox.Show("Delete?", "Confirm", MessageBoxButton.YesNo));

    public sealed class Model { public string Name { get; set; } = ""; }
}
