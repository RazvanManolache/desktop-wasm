// Copyright (c) 2026 The DesktopWasm Authors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.ObjectModel;
using DesktopWasm.Graphics;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Container-recycling behavior for VirtualizingStackPanel (Job2): clean reuse
/// under scroll, selection, data/template/style changes and collection
/// mutations. All heap assertions measure container allocations specifically
/// (ItemContainerGenerator.ContainersCreatedCount), never total heap bytes.
/// </summary>
[ResetAmbientState]
public sealed class ContainerRecyclingTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    private static ListBox VirtualizedList(IList<string> items, VirtualizingStackPanel panel)
    {
        return new ListBox
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = items,
            Height = 200,
            Width = 300
        };
    }

    private static void Layout(Control list)
    {
        list.Measure(new WpfSize(300, 200));
        list.Arrange(new WpfRect(0, 0, 300, 200));
    }

    private sealed class CountingDataTemplate(string xml) : DataTemplate(xml)
    {
        public int Inflations;
        public override UIElement LoadContent()
        {
            Inflations++;
            return base.LoadContent();
        }
    }

    private sealed class ParitySelector(DataTemplate even, DataTemplate odd) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            var text = item?.ToString() ?? string.Empty;
            var digit = text.Length > 0 ? text[^1] - '0' : 0;
            return digit % 2 == 0 ? even : odd;
        }
    }

    private sealed class ParityStyleSelector(Style even, Style odd) : StyleSelector
    {
        public override Style? SelectStyle(object item, DependencyObject container)
        {
            var text = item?.ToString() ?? string.Empty;
            var digit = text.Length > 0 ? text[^1] - '0' : 0;
            return digit % 2 == 0 ? even : odd;
        }
    }

    [Fact]
    public void Recycling_reuses_containers_across_forward_and_back_scroll_with_selection()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var list = VirtualizedList(Enumerable.Range(0, 5000).Select(i => $"Item {i}").ToList(), panel);
        list.SelectedIndex = 42;
        Layout(list);

        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(list);
        }
        var steady = list.ItemContainerGenerator.ContainersCreatedCount;
        Assert.True(steady > 0 && steady <= 30);

        for (var step = 4; step <= 40; step++)
        {
            panel.SetVerticalOffset(step * 24.0);
            Layout(list);
            Assert.Equal(steady, list.ItemContainerGenerator.ContainersCreatedCount);
            var first = panel.RealizedRange.StartIndex;
            var container = Assert.IsType<ListBoxItem>(
                list.ItemContainerGenerator.ContainerFromIndex(first));
            Assert.Equal($"Item {first}", container.Content);
            Assert.Equal(first == 42, container.IsSelected);
        }
        for (var step = 40; step >= 4; step--)
        {
            panel.SetVerticalOffset(step * 24.0);
            Layout(list);
            Assert.Equal(steady, list.ItemContainerGenerator.ContainersCreatedCount);
        }

        panel.SetVerticalOffset(panel.GetTotalExtent(42));
        Layout(list);
        Assert.True(list.ItemContainerGenerator.IsRealized(42));
        var selected = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(42));
        Assert.True(selected.IsSelected);
        Assert.Equal("Item 42", selected.Content);
    }

    [Fact]
    public void Recycling_survives_collection_add_remove_replace_and_move()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new ObservableCollection<string>(Enumerable.Range(0, 2000).Select(i => $"Row {i:D4}"));
        var list = VirtualizedList(source, panel);
        Layout(list);
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(list);
        }
        var steady = list.ItemContainerGenerator.ContainersCreatedCount;

        // Insert at the top: window contents shift, containers recycle. Scroll to
        // the top first: index 0 leaves the old window after an insert, and a
        // direct ContainerFromIndex outside the window would force-realize it
        // (WPF contract) and self-defeat the growth assertion below.
        source.Insert(0, "Row NEW-TOP");
        panel.SetVerticalOffset(0);
        Layout(list);
        Assert.Equal("Row NEW-TOP", Assert.IsType<ListBoxItem>(
            list.ItemContainerGenerator.ContainerFromIndex(0)).Content);
        Assert.Equal(steady, list.ItemContainerGenerator.ContainersCreatedCount);

        // Remove a realized row: container returns to the pool.
        var first = panel.RealizedRange.StartIndex;
        source.RemoveAt(first);
        Layout(list);
        Assert.Equal(steady, list.ItemContainerGenerator.ContainersCreatedCount);
        Assert.Equal(source[first], Assert.IsType<ListBoxItem>(
            list.ItemContainerGenerator.ContainerFromIndex(first)).Content);

        // Replace a realized row: same slot shows the new data.
        var replaceAt = panel.RealizedRange.StartIndex;
        source[replaceAt] = "Row REPLACED";
        Layout(list);
        Assert.Equal("Row REPLACED", Assert.IsType<ListBoxItem>(
            list.ItemContainerGenerator.ContainerFromIndex(replaceAt)).Content);

        // Move a row across the window: no loss, no duplication.
        var moveFrom = panel.RealizedRange.StartIndex;
        var moving = source[moveFrom];
        source.Move(moveFrom, moveFrom + 2);
        Layout(list);
        Assert.Equal(moving, Assert.IsType<ListBoxItem>(
            list.ItemContainerGenerator.ContainerFromIndex(moveFrom + 2)).Content);

        // Every realized container shows its current item; nothing missing.
        var (start, end) = (panel.RealizedRange.StartIndex, panel.RealizedRange.EndIndex);
        for (var i = start; i < end; i++)
            Assert.Equal(source[i], Assert.IsType<ListBoxItem>(
                list.ItemContainerGenerator.ContainerFromIndex(i)).Content);
    }

    [Fact]
    public void Recycling_reuses_templated_visuals_while_template_is_unchanged()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var template = new CountingDataTemplate($"<TextBlock {Ns} Text=\"{{Binding}}\" />");
        var list = VirtualizedList(Enumerable.Range(0, 5000).Select(i => $"Cell {i:D4}").ToList(), panel);
        list.ItemTemplate = template;
        Layout(list);
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(list);
        }

        template.Inflations = 0;
        for (var step = 4; step <= 24; step++)
        {
            panel.SetVerticalOffset(step * 24.0);
            Layout(list);
        }

        // Reused rows keep their inflated visual with a refreshed DataContext;
        // only genuinely new containers inflate (at most a window adjustment).
        Assert.True(template.Inflations <= 2,
            $"Expected near-zero re-inflations during steady scroll, got {template.Inflations}.");
        var first = panel.RealizedRange.StartIndex;
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(first));
        Assert.True(row.HasTemplatedContent);
        Assert.Equal($"Cell {first:D4}", ((FrameworkElement)row.TemplatedContent!).DataContext);
    }

    [Fact]
    public void Recycling_picks_up_item_template_change()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var templateA = new DataTemplate($"<TextBlock {Ns} Text=\"{{Binding}}\" Tag=\"A\" />");
        var templateB = new DataTemplate($"<TextBlock {Ns} Text=\"{{Binding}}\" Tag=\"B\" />");
        var list = VirtualizedList(Enumerable.Range(0, 1000).Select(i => $"Cell {i}").ToList(), panel);
        list.ItemTemplate = templateA;
        Layout(list);
        var first = panel.RealizedRange.StartIndex;
        Assert.Equal("A", ((FrameworkElement)Assert.IsType<ListBoxItem>(
            list.ItemContainerGenerator.ContainerFromIndex(first)).TemplatedContent!).Tag);

        list.ItemTemplate = templateB;
        Layout(list);
        first = panel.RealizedRange.StartIndex;
        var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(first));
        Assert.Equal("B", ((FrameworkElement)row.TemplatedContent!).Tag);
    }

    [Fact]
    public void Recycling_reapplies_style_selector_choice_on_reused_containers()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var styleA = new Style(typeof(Label));
        styleA.Setters.Add(new Setter(FrameworkElement.TagProperty, "AlphaStyle"));
        var styleB = new Style(typeof(Label));
        styleB.Setters.Add(new Setter(FrameworkElement.TagProperty, "BetaStyle"));
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 2000).Select(i => $"S{i}").ToList(),
            ItemContainerStyleSelector = new ParityStyleSelector(styleA, styleB),
            Height = 200,
            Width = 300
        };
        Layout(control);
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(control);
        }

        // Scroll far enough that containers recycle across the even/odd boundary,
        // at both parities (an even-only scroll can align old/new parities by luck).
        foreach (var target in new[] { 500, 501 })
        {
            panel.SetVerticalOffset(panel.GetTotalExtent(target));
            Layout(control);
            var (start, end) = (panel.RealizedRange.StartIndex, panel.RealizedRange.EndIndex);
            Assert.True(end - start > 2);
            for (var i = start; i < end; i++)
            {
                var container = Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(i));
                var expected = i % 2 == 0 ? "AlphaStyle" : "BetaStyle";
                Assert.Equal(expected, container.Tag);
            }
            Assert.True(control.ItemContainerGenerator.ContainersCreatedCount <= 60);
        }
    }

    [Fact]
    public void Recycling_survives_collection_mutations_on_items_control()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var source = new ObservableCollection<string>(Enumerable.Range(0, 2000).Select(i => $"Cell {i:D4}"));
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = source,
            Height = 200,
            Width = 300
        };
        Layout(control);
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(control);
        }
        var steady = control.ItemContainerGenerator.ContainersCreatedCount;

        source.Insert(0, "Cell NEW");
        panel.SetVerticalOffset(0);
        Layout(control);
        Assert.Equal("Cell NEW", Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0)).Content);
        // One window refill at most per source rebuild; no per-item leak.
        Assert.True(control.ItemContainerGenerator.ContainersCreatedCount <= steady + 15);

        var first = panel.RealizedRange.StartIndex;
        source.RemoveAt(first);
        Layout(control);
        Assert.True(control.ItemContainerGenerator.ContainersCreatedCount <= steady + 30);
        Assert.Equal(source[first], Assert.IsType<Label>(
            control.ItemContainerGenerator.ContainerFromIndex(first)).Content);
    }

    [Fact]
    public void Recycling_updates_display_member_path_on_change()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            Height = 200,
            Width = 300
        };
        control.AddItem(new { Name = "Alice", Code = "A1" });
        control.AddItem(new { Name = "Bob", Code = "B2" });
        control.DisplayMemberPath = "Name";
        Layout(control);
        Assert.Equal("Alice", Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0)).Content);

        control.DisplayMemberPath = "Code";
        Layout(control);
        Assert.Equal("A1", Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(0)).Content);
        Assert.Equal("B2", Assert.IsType<Label>(control.ItemContainerGenerator.ContainerFromIndex(1)).Content);
    }

    [Fact]
    public void Recycling_selector_reuses_containers_with_matching_template_kind()
    {
        var panel = new VirtualizingStackPanel { VirtualizationMode = VirtualizationMode.Recycling };
        var evenTemplate = new DataTemplate($"<TextBlock {Ns} Text=\"{{Binding}}\" Tag=\"Even\" />");
        var oddTemplate = new DataTemplate($"<Label {Ns} Content=\"{{Binding}}\" Tag=\"Odd\" />");
        var control = new ItemsControl
        {
            ItemsPanel = new ItemsPanelTemplate(() => panel),
            ItemsSource = Enumerable.Range(0, 2000).Select(i => $"V{i}").ToList(),
            ItemTemplateSelector = new ParitySelector(evenTemplate, oddTemplate),
            Height = 200,
            Width = 300
        };
        Layout(control);
        for (var i = 1; i <= 3; i++)
        {
            panel.SetVerticalOffset(i * 24.0);
            Layout(control);
        }

        // Scroll far: recycled containers must keep serving their template kind.
        panel.SetVerticalOffset(panel.GetTotalExtent(600));
        Layout(control);
        var (start, end) = (panel.RealizedRange.StartIndex, panel.RealizedRange.EndIndex);
        for (var i = start; i < end; i++)
        {
            var container = control.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(container);
            var tag = (container as FrameworkElement)?.Tag?.ToString();
            Assert.Equal(i % 2 == 0 ? "Even" : "Odd", tag);
        }
    }
}
