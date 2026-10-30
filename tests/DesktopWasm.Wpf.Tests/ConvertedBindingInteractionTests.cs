using System.Text.Json;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// End-to-end proof that the converted SimpleBinding package (staged under
/// wwwroot/apps/simple-binding, served on demand as apps/simple-binding)
/// boots through the shim and pushes live TwoWay binding updates: typing
/// into the editor updates the Person model and the bound output TextBlock.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class ConvertedBindingInteractionTests
{
    private sealed class BindingHarness
    {
        public WasmInputRouter Router { get; } = new();
        public WpfInputManager Manager { get; }
        public Window Window { get; }
        public TextBox Editor { get; }
        public TextBlock Output { get; }
        public object Model { get; }

        public BindingHarness(Window window)
        {
            Window = window;
            Manager = new WpfInputManager(Router);
            Manager.AddRoot(Window);
            Window.Measure(new WpfSize(800, 600));
            Window.Arrange(new WpfRect(0, 0, 800, 600));
            var elements = Descendants(Window).ToList();
            Editor = elements.OfType<TextBox>().Single();
            Output = elements.OfType<TextBlock>().Single(t => t.Text.Length > 0);
            Assert.True(Window.Resources.TryGetValue("MyDataSource", out var resource));
            Model = resource!;
        }

        public void ClickEditor()
        {
            var rect = Editor.ArrangedRect;
            var handle = new WasmWindowHandle(1);
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, true)
                { Kind = WasmPointerKind.Down, PointerId = 1 });
            Router.DispatchPointer(new WasmPointerArgs(handle, (float)(rect.X + rect.Width / 2), (float)(rect.Y + rect.Height / 2), WasmPointerButton.Left, false)
                { Kind = WasmPointerKind.Up, PointerId = 1 });
        }

        public void Key(string key) =>
            Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

        public void Type(string text)
        {
            foreach (var ch in text)
                Router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), ch.ToString(), true, false, false, false));
        }
    }

    [Fact]
    public async Task Converted_binding_boots_with_resource_model_and_labels()
    {
        var harness = new BindingHarness(await BootStagedBinding());
        Assert.Equal("SimpleBinding.MainWindow", harness.Window.GetType().FullName);
        Assert.Equal("Joe", harness.Editor.Text);
        Assert.Equal("Joe", harness.Output.Text);
        var labels = Descendants(harness.Window).OfType<Label>().Select(l => l.Content?.ToString()).ToList();
        Assert.Equal(new[] { "Enter a Name:", "The name you entered:" }, labels);
    }

    [Fact]
    public async Task Converted_binding_pushes_typed_text_to_model_and_output()
    {
        var harness = new BindingHarness(await BootStagedBinding());
        harness.ClickEditor();
        Assert.True(harness.Editor.IsFocused);
        harness.Key("End");
        Assert.Equal(3, harness.Editor.CaretIndex);
        harness.Key("Backspace"); harness.Key("Backspace"); harness.Key("Backspace");
        Assert.Equal("", harness.Editor.Text);
        harness.Type("Ann");
        Assert.Equal("Ann", harness.Editor.Text);
        Assert.Equal("Ann", harness.Model.GetType().GetProperty("PersonName")!.GetValue(harness.Model));
        Assert.Equal("Ann", harness.Output.Text);
    }

    private static async Task<Window> BootStagedBinding()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "simple-binding");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var loader = await ApplicationPackageLoader.CreateAsync(package, "simple-binding", file => File.ReadAllBytesAsync(System.IO.Path.Combine(apps, file)));
        return loader.Boot();
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }
}
