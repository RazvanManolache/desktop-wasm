using DesktopWasm.BrowserHost;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

[ResetAmbientState]
public sealed class StudioDemoTests
{
    [Fact]
    public void Baseline_covers_every_runnable_catalog_entry()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory.FullName, "src/DesktopWasm.BrowserHost/wwwroot/app-library.json")));
        using var baseline = System.Text.Json.JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory.FullName, "benchmarks/baselines/latest.json")));
        foreach (var app in catalog.RootElement.EnumerateArray().Where(a => a.GetProperty("status").GetString() == "ready" && a.GetProperty("kind").GetString() != "diagnostic"))
        {
            var slug = app.GetProperty("slug").GetString()!;
            Assert.True(baseline.RootElement.GetProperty("demos").TryGetProperty(slug, out var metrics), $"Missing real browser baseline for {slug}; run benchmarks/bench.py --update-baseline after validation.");
            Assert.True(metrics.GetProperty("paintProof").GetProperty("nonWhitePixels").GetInt32() > 50);
            Assert.True(metrics.GetProperty("dataLen").GetInt32() > 20000);
        }
        var checks = baseline.RootElement.GetProperty("launcherChecks");
        foreach (var name in new[] { "homeHasNoRuntime", "searchWorks", "emptySearch", "blockedTargetDoesNotBoot", "menuSearchDuringLoad",
            "rapidSwitchKeepsLatestSession", "measuredStatistics", "appCanvasPainted", "liveCanvasScreenshot", "stopReleasesRuntime",
            "unknownRouteDoesNotBoot", "errorReportNamesApplicationAndSession", "failureDoesNotPoisonNextApp", "convertedAppLoadsOnDemand", "noLauncherScriptErrors" })
            Assert.True(checks.GetProperty(name).GetBoolean(), name);
    }

    [Theory]
    [InlineData("12+3=", "15")]
    [InlineData("5÷0=", "Error")]
    [InlineData("1.5×2=", "3.0")]
    [InlineData("8−3+2=", "7")]
    public void Calculator_arithmetic_and_errors(string keys, string expected)
    {
        var calculator = new CalculatorModel();
        var result = "";
        foreach (var key in keys) result = calculator.Press(key.ToString());
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Calculator_memory_clear_backspace_and_error_recovery()
    {
        var calculator = new CalculatorModel();
        calculator.Press("9"); calculator.Press("M+"); calculator.Press("C");
        Assert.Equal("9", calculator.Press("MR"));
        calculator.Press("MC"); Assert.Equal("0", calculator.Press("MR"));
        calculator.Press("1"); calculator.Press("2"); Assert.Equal("1", calculator.Press("⌫"));
        calculator.Press("÷"); calculator.Press("0"); Assert.Equal("Error", calculator.Press("="));
        Assert.Equal("4", calculator.Press("4"));
        calculator.Press("AC"); Assert.Equal("0", calculator.Press("MR"));
    }

    [Theory]
    [InlineData("pocket-calculator")]
    [InlineData("project-planner")]
    [InlineData("asset-explorer")]
    [InlineData("color-lab")]
    public void Mini_apps_build_fresh_trees_and_render_real_commands(string slug)
    {
        var demo = DemoCatalog.Find(slug);
        var first = demo.Build(); var second = demo.Build();
        Assert.NotSame(first, second);
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(first, new WpfSize(800, 600));
        var frame = host.Tick(new FrameContext(1, 0));
        using var commands = System.Text.Json.JsonDocument.Parse(frame.Json);
        Assert.Contains(commands.RootElement.EnumerateArray(), command => command[0].GetInt32() == 3 && command[1].GetString() == first.Title);
        Assert.True(frame.Json.Length > 500);
    }

    [Fact]
    public void Project_planner_actions_change_actual_rows_and_summary()
    {
        var window = DemoCatalog.Find("project-planner").Build();
        var elements = Descendants(window).ToList();
        var list = Assert.Single(elements.OfType<ListBox>());
        var entry = Assert.Single(elements.OfType<TextBox>());
        var summary = elements.OfType<TextBlock>().Single(t => t.Text.EndsWith("completed"));
        Button Button(string text) => elements.OfType<Button>().Single(b => Equals(b.Content, text));
        entry.Text = "New work"; Button("Add task").Command!.Execute(null);
        Assert.Equal(5, list.Items.Count); Assert.Equal("New work", list.SelectedItem!.Content);
        Button("Toggle done").Command!.Execute(null);
        Assert.Equal("✓ New work", list.SelectedItem.Content); Assert.Contains("1 completed", summary.Text);
        Button("Toggle done").Command!.Execute(null); Assert.Equal("New work", list.SelectedItem.Content);
        Button("Remove").Command!.Execute(null); Assert.Equal(4, list.Items.Count);
        entry.Text = "   "; Button("Add task").Command!.Execute(null); Assert.Equal(4, list.Items.Count);
    }

    [Fact]
    public void Color_lab_selection_changes_swatch_and_visible_hex_value()
    {
        var elements = Descendants(DemoCatalog.Find("color-lab").Build()).ToList();
        elements.OfType<Button>().Single(b => Equals(b.Content, "Ocean")).Command!.Execute(null);
        Assert.Contains(elements.OfType<Border>(), b => b.BackgroundRgba == 0x3169A0FF);
        Assert.Contains(elements.OfType<TextBlock>(), t => t.Text == "Ocean · #3169A0");
    }

    [Fact]
    public void Validation_links_lab_blocks_bad_input_and_navigates_in_session()
    {
        var window = DemoCatalog.Find("validation-links").Build();
        var elements = Descendants(window).ToList();
        var editor = Assert.Single(elements.OfType<TextBox>());
        var link = Assert.Single(elements.OfType<Hyperlink>());
        var viewer = Assert.Single(elements.OfType<Frame>());

        editor.Text = "Ann3";
        Assert.True(Validation.GetHasError(editor));
        Assert.Contains(elements.OfType<TextBlock>(), t => t.Text.StartsWith("Name: invalid"));
        Assert.Contains(elements.OfType<TextBlock>(), t => t.Text.StartsWith("Error event: Added"));

        editor.Text = "Ann";
        Assert.False(Validation.GetHasError(editor));
        Assert.Contains(elements.OfType<TextBlock>(), t => t.Text == "Name: \"Ann\" (valid)");

        var navReadout = elements.OfType<TextBlock>().Single(t => t.Text.StartsWith("Viewing:"));
        Assert.Equal("Viewing: Home", navReadout.Text);
        link.DoClick();
        Assert.Equal("Details", ((Page)viewer.Content!).Title);
        Assert.Equal("Viewing: Details", navReadout.Text);
        elements.OfType<Button>().Single(b => Equals(b.Content, "Back home")).RaiseClick();
        Assert.Equal("Home", ((Page)viewer.Content!).Title);
        Assert.Equal("Viewing: Home", navReadout.Text);
    }

    [Fact]
    public void Scroll_lab_readout_follows_scroll_changed()
    {
        var window = DemoCatalog.Find("scroll").Build();
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        host.Tick(new FrameContext(1, 0));
        var elements = Descendants(window).ToList();
        var view = Assert.Single(elements.OfType<ScrollViewer>());
        var readout = elements.OfType<TextBlock>().Single(t => t.Text.StartsWith("Offset:"));
        // Initial layout already fired once (extent/viewport transition, zero offset delta).
        Assert.Equal("Offset: 0 (Δ 0)", readout.Text);

        view.ScrollToBottom();

        Assert.True(view.ScrollableHeight > 0);
        Assert.Equal(view.ScrollableHeight, view.VerticalOffset);
        Assert.Equal($"Offset: {view.VerticalOffset:0} (Δ {view.VerticalOffset:0})", readout.Text);
    }

    [Fact]
    public void Grid_rows_lab_reports_selection_cells_and_sort()
    {
        var window = DemoCatalog.Find("datagrid").Build();
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        GridRowsLab.Current?.Wire(host);
        Assert.NotNull(GridRowsLab.Current);
        var lab = GridRowsLab.Current;
        Assert.Equal("Picked: Grace, 85", lab.Header.Text);
        Assert.Equal("First: Ada, 36 · Cell: 36", lab.FirstReadout.Text);
        Assert.Equal("Row: 0/3 · Details: yes", lab.RowReadout.Text);
        Assert.Equal("Sort: none", lab.SortButton.Content);

        lab.SortButton.RaiseClick();
        Assert.Equal("Sort: age up", lab.SortButton.Content);
        Assert.Equal("First: Ada, 36 · Cell: 36", lab.FirstReadout.Text);
        lab.SortButton.RaiseClick();
        Assert.Equal("Sort: age down", lab.SortButton.Content);
        Assert.Equal("First: Katherine, 101 · Cell: 101", lab.FirstReadout.Text);
        Assert.Equal("Row: 0/3 · Details: yes", lab.RowReadout.Text);
        lab.SortButton.RaiseClick();
        Assert.Equal("Sort: none", lab.SortButton.Content);
        Assert.Equal("First: Ada, 36 · Cell: 36", lab.FirstReadout.Text);

        // The container behind the readout is the real generated row.
        var row = Assert.IsType<DataGridRow>(lab.Grid.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(0, row.GetIndex());
        Assert.IsType<DataGridCellsPresenter>(row.CellsPresenter);
        Assert.IsType<DataGridDetailsPresenter>(row.DetailsPresenter);

        var frame = host.Tick(new FrameContext(1, 0));
        Assert.True(frame.Json.Length > 500);
    }

    [Fact]
    public void Shell_lab_reports_size_progress_action_and_split()
    {
        var window = DemoCatalog.Find("shell").Build();
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        ShellLab.Current?.Wire(host);
        Assert.NotNull(ShellLab.Current);
        var lab = ShellLab.Current;
        Assert.Equal(SizeToContent.WidthAndHeight, lab.Window.SizeToContent);
        Assert.Equal("Size: WidthAndHeight", lab.SizeReadout.Text);
        Assert.Equal("Progress: Normal 45%", lab.ProgressReadout.Text);
        Assert.Equal("Action: idle", lab.ActionReadout.Text);
        Assert.Equal("Split: 200|4|*", lab.SplitReadout.Text);
        Assert.Equal(3, lab.Window.TaskbarItemInfo.ThumbButtonInfos.Count);

        lab.BumpButton.RaiseClick();
        Assert.Equal("Progress: Normal 55%", lab.ProgressReadout.Text);
        lab.RecordButton.RaiseClick();
        Assert.Equal("Action: recording", lab.ActionReadout.Text);
        lab.FailButton.RaiseClick();
        Assert.Equal(TaskbarItemProgressState.Error, lab.Window.TaskbarItemInfo.ProgressState);
        Assert.Equal("Progress: Error 55%", lab.ProgressReadout.Text);

        var frame = host.Tick(new FrameContext(1, 0));
        Assert.True(frame.Json.Length > 500);
    }

    [Fact]
    public void Effects_lab_reports_shadow_mask_page_and_container()
    {
        var window = DemoCatalog.Find("effects").Build();
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        EffectsLab.Current?.Wire(host);
        Assert.NotNull(EffectsLab.Current);
        var lab = EffectsLab.Current;
        Assert.Equal("Shadow: on", lab.ShadowReadout.Text);
        Assert.Equal("Blur: 4", lab.BlurReadout.Text);
        Assert.Equal("Container ops: 1", lab.ContainerReadout.Text);

        lab.ToggleButton.RaiseClick();
        Assert.Equal("Shadow: off", lab.ShadowReadout.Text);
        Assert.Null(lab.ShadowBox.Effect);
        lab.ToggleButton.RaiseClick();
        Assert.Equal("Shadow: on", lab.ShadowReadout.Text);

        lab.BlurButton.RaiseClick();
        Assert.Equal("Blur: 6", lab.BlurReadout.Text);
        Assert.Equal(6, ((DropShadowEffect)lab.ShadowBox.Effect!).BlurRadius);

        lab.SecretBox.Password = "s3cret";
        Assert.Equal("Typed: 6 chars", lab.TypedReadout.Text);
        Assert.Equal("Masked: ******", lab.MaskedReadout.Text);

        lab.GoButton.RaiseClick();
        Assert.Equal("Page: https://example.com/", lab.PageReadout.Text);
        Assert.Equal("Back: no", lab.BackReadout.Text);

        Assert.Equal("Tile: Center/Center", lab.TileReadout.Text);
        lab.AlignXButton.RaiseClick();
        Assert.Equal("Tile: Right/Center", lab.TileReadout.Text);
        Assert.Equal(AlignmentX.Right, lab.TileSample.AlignmentX);
        lab.AlignYButton.RaiseClick();
        Assert.Equal("Tile: Right/Bottom", lab.TileReadout.Text);

        Assert.Equal("Power: -", lab.PowerReadout.Text);
        lab.PowerButton.RaiseClick();
        Assert.StartsWith("Power: Suspend x", lab.PowerReadout.Text);

        Assert.Equal("Media: -", lab.MediaReadout.Text);
        lab.PlayButton.RaiseClick();
        Assert.Equal("Media: failed InvalidOperationException", lab.MediaReadout.Text);
        lab.OpenButton.RaiseClick();
        Assert.Equal("Media: opened clip.mp4", lab.MediaReadout.Text);
        lab.PlayButton.RaiseClick();
        Assert.Equal("Media: playing clip.mp4", lab.MediaReadout.Text);
        lab.PauseButton.RaiseClick();
        Assert.Equal("Media: paused clip.mp4", lab.MediaReadout.Text);

        var frame = host.Tick(new FrameContext(1, 0));
        Assert.True(frame.Json.Length > 500);
    }

    [Fact]
    public void Converted_loader_rejects_stale_or_missing_map_before_loading_bytes()
    {
        Assert.Throws<InvalidOperationException>(() => DemoCatalog.BootConverted([], "{}"));
        Assert.Throws<InvalidOperationException>(() => DemoCatalog.BootConverted([], "{\"MapVersion\":\"stale\"}"));
    }

    [Fact]
    public void Drag_drop_lab_runs_enter_drop_paste_and_dpi()
    {
        var window = DemoCatalog.Find("drag-drop").Build();
        var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
        host.AddWindow(window, new WpfSize(800, 600));
        DragDropLab.Current?.Wire(host);
        Assert.NotNull(DragDropLab.Current);
        var lab = DragDropLab.Current;

        lab.SimDropButton.RaiseClick();
        Assert.Contains("Copy", lab.EffectReadout.Text);
        Assert.Contains("Dropped 2 file(s): clip.gif, frame.png", lab.FilesReadout.Text);
        Assert.True(WasmVirtualFileSystem.Current.Exists("lab-drops/clip.gif"));

        lab.SimHoverButton.RaiseClick();
        Assert.Contains("None", lab.EffectReadout.Text);

        lab.PasteBadButton.RaiseClick();
        Assert.Contains("vetoed", lab.PasteReadout.Text);
        lab.PasteGoodButton.RaiseClick();
        Assert.Equal("42", lab.PasteBox.Text);
        Assert.Contains("Pasted", lab.PasteReadout.Text);

        Assert.Contains("96", lab.DpiReadout.Text);

        var frame = host.Tick(new FrameContext(1, 0));
        Assert.True(frame.Json.Length > 500);
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }
}
