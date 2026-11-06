using DesktopWasm.Core;
using System.ComponentModel;

namespace DesktopWasm.Wpf.Tests;

/// <summary>ComboBox original-dropdown model binding (WC1925 gate). The gallery
/// ComboBox page binds font families/sizes over ItemsSource with an inline-items
/// combo and an editable combo. These tests prove a UI-driven open -&gt; keyboard
/// navigate -&gt; commit/cancel round-trip pushes TwoWay model bindings, mirroring
/// the inline Blue/Green/Red/Yellow sample and the bound font-list samples.</summary>
[ResetAmbientState]
public sealed class ComboBoxModelBindingTests
{
    private sealed class Choice : INotifyPropertyChanged
    {
        private string? _pick;
        private int _size;

        public string? Pick
        {
            get => _pick;
            set
            {
                if (_pick == value) return;
                _pick = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pick)));
            }
        }

        public int Size
        {
            get => _size;
            set
            {
                if (_size == value) return;
                _size = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class FontOption
    {
        public string Name { get; set; } = string.Empty;
        public int Size { get; set; }
    }

    private static (WasmInputRouter Router, ComboBox Box) Harness(ComboBox combo)
    {
        var router = new WasmInputRouter();
        var manager = new WpfInputManager(router);
        var root = new StackPanel();
        root.AddChild(combo);
        manager.AddRoot(root);
        root.Measure(new WpfSize(400, 400));
        root.Arrange(new WpfRect(0, 0, 400, 400));
        Assert.True(manager.TryFocus(combo));
        return (router, combo);
    }

    private static void Key(WasmInputRouter router, string key) =>
        router.DispatchKeyboard(new WasmKeyArgs(new WasmWindowHandle(1), key, true, false, false, false));

    [Fact]
    public void Inline_rows_keyboard_commit_pushes_selected_value_to_model()
    {
        // Sample shape: Blue/Green/Red/Yellow inline rows, SelectedIndex=0.
        var vm = new Choice();
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        combo.AddItem(new ComboBoxItem { Content = "Red" });
        combo.AddItem(new ComboBoxItem { Content = "Yellow" });
        combo.SelectedValuePath = "Content";
        combo.DataContext = vm;
        combo.SetBinding("SelectedValue", new Binding("Pick"));
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal("Blue", vm.Pick);

        Key(router, "F4");
        Assert.True(combo.IsDropDownOpen);
        Key(router, "ArrowDown");
        Assert.Equal(1, combo.SelectedIndex);
        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Green", vm.Pick);
        Assert.Equal("Green", combo.Text);
    }

    [Fact]
    public void ItemsSource_rows_keyboard_commit_pushes_selected_value_to_model()
    {
        // Sample shape: bound font list with display/value paths.
        var vm = new Choice();
        var combo = new ComboBox();
        combo.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<FontOption>
        {
            new() { Name = "Arial", Size = 8 },
            new() { Name = "Segoe UI", Size = 12 },
            new() { Name = "Times New Roman", Size = 16 },
        };
        combo.DisplayMemberPath = "Name";
        combo.SelectedValuePath = "Size";
        combo.DataContext = vm;
        combo.SetBinding("SelectedValue", new Binding("Size"));
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal(8, vm.Size);
        Assert.Equal("Arial", combo.Text);

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "ArrowDown");
        Assert.Equal(2, combo.SelectedIndex);
        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(16, vm.Size);
        Assert.Equal("Times New Roman", combo.Text);
    }

    [Fact]
    public void Editable_commit_syncs_text_and_default_binding_to_model()
    {
        // Editable sample shape (font sizes): the committed selection
        // rewrites the editable text, and the default (TwoWay, like WPF)
        // Text binding carries it to the model.
        var vm = new Choice();
        var combo = new ComboBox { IsEditable = true };
        combo.ItemsSource = new List<string> { "Arial", "Segoe UI", "Times New Roman" };
        combo.DataContext = vm;
        combo.SetBinding("Text", new Binding("Pick"));
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal("Arial", combo.Text);
        Assert.Equal("Arial", vm.Pick);

        Key(router, "F4");
        Assert.True(combo.IsDropDownOpen);
        Key(router, "ArrowDown");
        Key(router, "Enter");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Segoe UI", combo.Text);
        Assert.Equal("Segoe UI", vm.Pick);
    }

    [Fact]
    public void Editable_escape_restores_text_and_model_value()
    {
        var vm = new Choice();
        var combo = new ComboBox { IsEditable = true };
        combo.ItemsSource = new List<string> { "Arial", "Segoe UI", "Times New Roman" };
        combo.DataContext = vm;
        combo.SetBinding("Text", new Binding("Pick"));
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal("Arial", vm.Pick);

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "ArrowDown");
        Assert.Equal("Times New Roman", combo.Text);
        Key(router, "Escape");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal("Arial", combo.Text);
        Assert.Equal("Arial", vm.Pick);
    }

    [Fact]
    public void Editable_programmatic_selection_syncs_text_and_model()
    {
        var vm = new Choice();
        var combo = new ComboBox { IsEditable = true };
        combo.ItemsSource = new List<string> { "Arial", "Segoe UI", "Times New Roman" };
        combo.DataContext = vm;
        combo.SetBinding("Text", new Binding("Pick"));
        Harness(combo);

        combo.SelectedIndex = 2;
        Assert.Equal("Times New Roman", combo.Text);
        Assert.Equal("Times New Roman", vm.Pick);

        combo.SelectedIndex = -1;
        Assert.Equal(string.Empty, combo.Text);
        Assert.Equal(string.Empty, vm.Pick);
    }

    [Fact]
    public void Noneditable_text_still_tracks_live_selection()
    {
        // Guard: the non-editable display path (getter-backed) is untouched
        // by the editable sync.
        var combo = new ComboBox();
        combo.AddItem(new ComboBoxItem { Content = "Blue" });
        combo.AddItem(new ComboBoxItem { Content = "Green" });
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal("Blue", combo.Text);

        Key(router, "F4");
        Key(router, "ArrowDown");
        Key(router, "Enter");

        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("Green", combo.Text);
    }

    [Fact]
    public void Escape_restores_model_value_to_preopen_selection()
    {
        var vm = new Choice();
        var combo = new ComboBox();
        combo.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<FontOption>
        {
            new() { Name = "Arial", Size = 8 },
            new() { Name = "Segoe UI", Size = 12 },
            new() { Name = "Times New Roman", Size = 16 },
        };
        combo.DisplayMemberPath = "Name";
        combo.SelectedValuePath = "Size";
        combo.DataContext = vm;
        combo.SetBinding("SelectedValue", new Binding("Size"));
        var (router, _) = Harness(combo);
        combo.SelectedIndex = 0;
        Assert.Equal(8, vm.Size);

        Key(router, "F4");
        Key(router, "ArrowDown");
        Assert.Equal(12, vm.Size);
        Key(router, "Escape");

        Assert.False(combo.IsDropDownOpen);
        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal(8, vm.Size);
        Assert.Equal("Arial", combo.Text);
    }
}
