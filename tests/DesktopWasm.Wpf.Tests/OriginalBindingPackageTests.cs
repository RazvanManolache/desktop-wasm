using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class OriginalBindingPackageTests
{
    [Fact]
    public async Task Original_application_edit_updates_its_resource_model_and_other_bound_control()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "DesktopWasm.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = System.IO.Path.Combine(root.FullName, "src/DesktopWasm.BrowserHost/wwwroot/apps/simple-binding");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(File.ReadAllText(System.IO.Path.Combine(directory, "app-manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var old = Application.Current;
        try
        {
            // Both originals embed MainWindow.xaml. A previous app must not
            // supply its resource to the next component's InitializeComponent.
            WpfAppBoot.Boot(File.ReadAllBytes(System.IO.Path.Combine(root.FullName, "src/DesktopWasm.BrowserHost/ConvertedApps/CalculatorDemo/CalculatorDemo.dll")));
            using var loader = await ApplicationPackageLoader.CreateAsync(package, "simple-binding", file => File.ReadAllBytesAsync(System.IO.Path.Combine(directory, file)));
            var window = loader.Boot();
            Assert.Equal("SimpleBinding.MainWindow", window.GetType().FullName);
            var border = Assert.IsType<Border>(window.Content);
            Assert.Equal(new Thickness(8), border.Padding);
            Assert.Equal(0x00FFFFFFu, border.BorderRgba);
            var panel = Assert.IsType<DockPanel>(border.LogicalChildren.Single());
            var editor = Assert.Single(panel.LogicalChildren.OfType<TextBox>());
            var output = Assert.Single(panel.LogicalChildren.OfType<TextBlock>());
            Assert.Equal("Joe", editor.Text); Assert.Equal("Joe", output.Text);
            Assert.Equal(100, editor.Width); Assert.Equal(25, editor.Height);
            Assert.Equal(Dock.Top, DockPanel.GetDock(editor));
            Assert.Equal(new[] { "Enter a Name:", "The name you entered:" }, panel.LogicalChildren.OfType<Label>().Select(label => label.Content));
            var model = window.Resources.TryGetValue("MyDataSource", out var resource) ? resource : null;
            Assert.NotNull(model);
            editor.Text = "Grace";
            Assert.Equal("Grace", model.GetType().GetProperty("PersonName")!.GetValue(model));
            Assert.Equal("Grace", output.Text);
            Assert.Contains(WasmMilCore.RenderHeadless(window, new WpfSize(800, 600)).OfType<DrawCommand.DrawText>(), command => command.Text == "Grace");
            Assert.Equal(100, editor.RenderRect.Width); Assert.Equal(25, editor.RenderRect.Height);
            Assert.Equal(200, panel.RenderRect.Width); Assert.Equal(100, panel.RenderRect.Height);
            Assert.True(panel.RenderRect.X >= 40, "Border child margin/padding were lost during arrange.");
        }
        finally { Application.Current = old; }
    }
}
