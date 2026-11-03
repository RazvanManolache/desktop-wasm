// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Wpf;
using Xunit;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Verifies cost and correctness of large-list data virtualization across
/// real converted and engine ListBox/ItemsControl/ListView in headless rendering.
/// Covers before/after cost comparisons and confirms no hidden or dropped items across scroll windows.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class LargeListVirtualizationCostAndCorrectnessTests : IDisposable
{
    private readonly Application? _previous = Application.Current;

    public LargeListVirtualizationCostAndCorrectnessTests()
    {
        XamlLoader.CustomElementSource = null;
    }

    public void Dispose()
    {
        XamlLoader.CustomElementSource = null;
        Application.Current = _previous;
    }

    [Fact]
    public void ListBox_large_list_cost_before_and_after_virtualization()
    {
        const int totalItems = 5000;
        var items = Enumerable.Range(0, totalItems).Select(i => $"Item {i:D4}").ToList();

        // --- BEFORE: Non-virtualized ListBox ---
        var listNonVirt = new ListBox
        {
            ItemsSource = items,
            Height = 200,
            Width = 300
        };

        var beforeSw = Stopwatch.StartNew();
        var beforeCommands = WasmMilCore.RenderHeadless(listNonVirt, new WpfSize(300, 200));
        beforeSw.Stop();

        var beforeContainers = listNonVirt.Items.Count;
        var beforeTextCommands = beforeCommands.OfType<DrawCommand.DrawText>().Count();

        // Non-virtualized must generate a container for every item
        Assert.Equal(totalItems, beforeContainers);
        Assert.True(beforeTextCommands >= totalItems, $"Expected >= {totalItems} text commands, got {beforeTextCommands}");

        // --- AFTER: Virtualized ListBox ---
        var panel = new VirtualizingStackPanel();
        var listVirt = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = items,
            Height = 200,
            Width = 300
        };

        var afterSw = Stopwatch.StartNew();
        var afterCommands = WasmMilCore.RenderHeadless(listVirt, new WpfSize(300, 200));
        afterSw.Stop();

        var afterContainers = listVirt.ItemContainerGenerator.RealizedCount;
        var afterTextCommands = afterCommands.OfType<DrawCommand.DrawText>().Count();

        // Virtualized must realize only a bounded window of containers
        Assert.True(afterContainers < 30, $"Expected < 30 realized containers, got {afterContainers}");
        Assert.True(afterTextCommands < 30, $"Expected < 30 text commands, got {afterTextCommands}");
        Assert.True(listVirt.Items.Count < 30);

        // Verification of cost reduction: > 99% reduction in containers and draw commands
        var containerReduction = (1.0 - ((double)afterContainers / beforeContainers)) * 100.0;
        var commandReduction = (1.0 - ((double)afterTextCommands / beforeTextCommands)) * 100.0;
        Assert.True(containerReduction > 99.0, $"Expected > 99% container reduction, got {containerReduction:F1}%");
        Assert.True(commandReduction > 99.0, $"Expected > 99% command reduction, got {commandReduction:F1}%");
    }

    [Fact]
    public void ListBox_large_list_correctness_scrolling_no_hidden_or_dropped_items()
    {
        const int totalItems = 5000;
        var items = Enumerable.Range(0, totalItems).Select(i => $"Entry {i:D4}").ToList();

        var panel = new VirtualizingStackPanel();
        var list = new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = items,
            Height = 200,
            Width = 300
        };

        // Window 1: Top of list (offset 0)
        var topCommands = WasmMilCore.RenderHeadless(list, new WpfSize(300, 200));
        var topTexts = topCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("Entry 0000", topTexts);
        Assert.Contains("Entry 0001", topTexts);
        Assert.DoesNotContain("Entry 2500", topTexts);
        Assert.DoesNotContain("Entry 4999", topTexts);
        Assert.True(list.ItemContainerGenerator.IsRealized(0));
        Assert.False(list.ItemContainerGenerator.IsRealized(2500));

        // Window 2: Middle of list (index 2500)
        panel.SetVerticalOffset(panel.GetTotalExtent(2500));
        var midCommands = WasmMilCore.RenderHeadless(list, new WpfSize(300, 200));
        var midTexts = midCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("Entry 2500", midTexts);
        Assert.Contains("Entry 2501", midTexts);
        Assert.DoesNotContain("Entry 0000", midTexts);
        Assert.DoesNotContain("Entry 4999", midTexts);
        Assert.True(list.ItemContainerGenerator.IsRealized(2500));
        Assert.False(list.ItemContainerGenerator.IsRealized(0));
        Assert.True(list.ItemContainerGenerator.RealizedCount < 30);

        // Window 3: Selection in virtualized list and bottom scroll
        list.SelectedIndex = 4995;
        Assert.Equal(4995, list.SelectedIndex);
        panel.SetVerticalOffset(panel.GetTotalExtent(totalItems) - 200);

        var bottomCommands = WasmMilCore.RenderHeadless(list, new WpfSize(300, 200));
        var bottomTexts = bottomCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("Entry 4995", bottomTexts);
        Assert.Contains("Entry 4999", bottomTexts);
        Assert.DoesNotContain("Entry 2500", bottomTexts);
        Assert.DoesNotContain("Entry 0000", bottomTexts);

        // Selected container 4995 must be realized and have selection flag
        Assert.True(list.ItemContainerGenerator.IsRealized(4995));
        var selContainer = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(4995));
        Assert.True(selContainer.IsSelected);

        // Selection highlight rectangle must be rendered in headless commands
        var rectCommands = bottomCommands.OfType<DrawCommand.FillRect>().ToList();
        Assert.Contains(rectCommands, r => r.Rgba == 0x0078D4FFu);

        // All items remain bounded throughout
        Assert.True(list.ItemContainerGenerator.RealizedCount < 30);
    }

    [Fact]
    public void Staged_gallery_ListBoxPage_large_list_cost_and_correctness()
    {
        using var staged = LoadStagedGalleryAssembly();
        var asm = staged.Assembly;
        var previous = XamlLoader.CustomElementSource;
        XamlLoader.CustomElementSource = asm;
        try
        {
            using var stream = asm.GetManifestResourceStream("Views/Collections/ListBoxPage.xaml")!;
            using var reader = new StreamReader(stream);
            var root = XamlLoader.Load(reader.ReadToEnd()).Root;
            var page = Assert.IsAssignableFrom<FrameworkElement>(root);

            // Construct 5,000 items in a ViewModel
            var largeItems = new ObservableCollection<string>(
                Enumerable.Range(0, 5000).Select(i => $"Font Family {i:D4}"));

            var vm = new LargeListGalleryViewModel { ListBoxItems = largeItems };
            page.DataContext = new PageStub { ViewModel = vm };

            var lists = Descendants(page).OfType<ListBox>().ToList();
            Assert.Equal(2, lists.Count);

            var boundList = lists[1];
            Assert.Equal(5000, boundList.ItemContainerGenerator.ContainerItems.Count);

            // Configure VirtualizingStackPanel items host
            var panel = new VirtualizingStackPanel();
            boundList.ItemsPanel = new ItemsPanelTemplate(() => panel);

            // Headless render of the converted bound ListBox
            var commands = WasmMilCore.RenderHeadless(boundList, new WpfSize(300, 200));
            var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

            // Headless output must contain initial visible items and be bounded
            Assert.Contains("Font Family 0000", texts);
            Assert.Contains("Font Family 0001", texts);
            Assert.DoesNotContain("Font Family 4000", texts);
            Assert.True(boundList.ItemContainerGenerator.RealizedCount < 30);

            // Selection navigation to middle item
            boundList.SelectedIndex = 2500;
            panel.SetVerticalOffset(panel.GetTotalExtent(2500));
            var scrolledCommands = WasmMilCore.RenderHeadless(boundList, new WpfSize(300, 200));
            var scrolledTexts = scrolledCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

            Assert.Contains("Font Family 2500", scrolledTexts);
            Assert.DoesNotContain("Font Family 0000", scrolledTexts);
            Assert.True(boundList.ItemContainerGenerator.IsRealized(2500));
            Assert.False(boundList.ItemContainerGenerator.IsRealized(0));
            Assert.True(boundList.ItemContainerGenerator.RealizedCount < 30);
        }
        finally
        {
            XamlLoader.CustomElementSource = previous;
        }
    }

    [Fact]
    public void ItemsControl_large_list_virtualization_cost_and_correctness_with_scrollviewer()
    {
        const int totalItems = 10000;
        var data = Enumerable.Range(0, totalItems).Select(i => $"RowData_{i:D5}").ToList();

        var panel = new VirtualizingStackPanel();
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = data
        };

        var scroller = new ScrollViewer
        {
            CanContentScroll = true,
            Height = 300,
            Width = 400,
            Content = control
        };

        // Render headless at top
        var topCommands = WasmMilCore.RenderHeadless(scroller, new WpfSize(400, 300));
        var topTexts = topCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("RowData_00000", topTexts);
        Assert.Contains("RowData_00001", topTexts);
        Assert.DoesNotContain("RowData_05000", topTexts);
        Assert.DoesNotContain("RowData_09999", topTexts);

        // Bounded container count: out of 10,000 items, < 40 containers realized
        Assert.Equal(totalItems, control.Items.Count);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 40);

        // Extent covers the entire 10,000 items
        Assert.True(scroller.ExtentHeight >= 200000);

        // Scroll to 5,000th item
        scroller.ScrollToVerticalOffset(panel.GetTotalExtent(5000));
        var midCommands = WasmMilCore.RenderHeadless(scroller, new WpfSize(400, 300));
        var midTexts = midCommands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("RowData_05000", midTexts);
        Assert.DoesNotContain("RowData_00000", midTexts);
        Assert.DoesNotContain("RowData_09999", midTexts);
        Assert.True(control.ItemContainerGenerator.RealizedCount < 40);
        Assert.False(control.ItemContainerGenerator.IsRealized(0));
        Assert.True(control.ItemContainerGenerator.IsRealized(5000));
    }

    [Fact]
    public void ListView_large_list_virtualization_and_selection_render()
    {
        const int totalItems = 5000;
        var data = Enumerable.Range(0, totalItems).Select(i => $"Record {i:D4}").ToList();

        var panel = new VirtualizingStackPanel();
        var listView = new ListView
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = data,
            Height = 240,
            Width = 350
        };

        listView.SelectedIndex = 1200;
        panel.SetVerticalOffset(panel.GetTotalExtent(1200));

        var commands = WasmMilCore.RenderHeadless(listView, new WpfSize(350, 240));
        var texts = commands.OfType<DrawCommand.DrawText>().Select(t => t.Text).ToList();

        Assert.Contains("Record 1200", texts);
        Assert.Contains("Record 1201", texts);
        Assert.DoesNotContain("Record 0000", texts);
        Assert.DoesNotContain("Record 4999", texts);

        Assert.True(listView.ItemContainerGenerator.IsRealized(1200));
        var selectedItem = Assert.IsType<ListViewItem>(listView.ItemContainerGenerator.ContainerFromIndex(1200));
        Assert.True(selectedItem.IsSelected);

        // Selection rectangle rendered
        Assert.Contains(commands.OfType<DrawCommand.FillRect>(), r => r.Rgba == 0x0078D4FFu);
        Assert.True(listView.ItemContainerGenerator.RealizedCount < 30);
    }

    private sealed class PageStub
    {
        public object? ViewModel { get; set; }
    }

    private sealed class LargeListGalleryViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<string> ListBoxItems { get; set; } = new();

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class StagedGallery : IDisposable
    {
        public System.Reflection.Assembly Assembly { get; }
        private readonly System.ResolveEventHandler _resolve;

        public StagedGallery(Dictionary<string, byte[]> images)
        {
            _resolve = (_, args) =>
            {
                var name = new System.Reflection.AssemblyName(args.Name).Name!;
                if (images.TryGetValue(name, out var bytes))
                {
                    try { return System.Reflection.Assembly.Load(bytes); }
                    catch { return null; }
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += _resolve;
            Assembly = System.Reflection.Assembly.Load(images["WPFGallery"]);
        }

        public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= _resolve;
    }

    private static StagedGallery LoadStagedGalleryAssembly()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "DesktopWasm.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var apps = System.IO.Path.Combine(directory.FullName, "src", "DesktopWasm.BrowserHost", "wwwroot", "apps", "wpf-gallery");
        var package = JsonSerializer.Deserialize<ApplicationPackage>(
            File.ReadAllText(System.IO.Path.Combine(apps, "app-manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return new StagedGallery(package.Assemblies.ToDictionary(
            a => a.Name,
            a => File.ReadAllBytes(System.IO.Path.Combine(apps, a.File)),
            StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;
        if (element is FrameworkElement framework)
            foreach (var child in framework.LogicalChildren)
                foreach (var item in Descendants(child)) yield return item;
    }
}
