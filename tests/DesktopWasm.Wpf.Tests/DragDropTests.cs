using System.Collections.Specialized;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// ScreenToGif drag-drop/data-object slice: the editor drops media/project
/// files onto drop targets (Control_DragEnter/Control_Drop), encoder items
/// copy images/paths through DataObject + Clipboard.SetDataObject, and the
/// numeric boxes veto pastes through DataObject.PastingEvent. Enum numerics,
/// format names and call shapes are pinned against the real WPF assemblies;
/// the HTML5 file-drop bridge raises these same routed events in the browser.
/// </summary>
[ResetAmbientState]
public sealed class DragDropTests
{
    [Fact]
    public void Drag_effect_and_key_state_values_match_wpf()
    {
        Assert.Equal(0, (int)DragDropEffects.None);
        Assert.Equal(1, (int)DragDropEffects.Copy);
        Assert.Equal(2, (int)DragDropEffects.Move);
        Assert.Equal(4, (int)DragDropEffects.Link);
        Assert.Equal(int.MinValue, (int)DragDropEffects.Scroll);
        Assert.Equal(-2147483645, (int)DragDropEffects.All);
        Assert.Equal(0, (int)DragDropKeyStates.None);
        Assert.Equal(1, (int)DragDropKeyStates.LeftMouseButton);
        Assert.Equal(2, (int)DragDropKeyStates.RightMouseButton);
        Assert.Equal(4, (int)DragDropKeyStates.ShiftKey);
        Assert.Equal(8, (int)DragDropKeyStates.ControlKey);
        Assert.Equal(16, (int)DragDropKeyStates.MiddleMouseButton);
        Assert.Equal(32, (int)DragDropKeyStates.AltKey);
    }

    [Fact]
    public void Data_format_names_match_wpf()
    {
        Assert.Equal("FileDrop", DataFormats.FileDrop);
        Assert.Equal("Text", DataFormats.Text);
        Assert.Equal("UnicodeText", DataFormats.UnicodeText);
        Assert.Equal("Rich Text Format", DataFormats.Rtf);
        Assert.Equal("HTML Format", DataFormats.Html);
        Assert.Equal("Xaml", DataFormats.Xaml);
        Assert.Equal("System.String", DataFormats.StringFormat);
        Assert.Equal("Bitmap", DataFormats.Bitmap);
    }

    [Fact]
    public void Data_object_round_trips_formats_like_the_editor_drop()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { "clip.gif" });
        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        Assert.False(data.GetDataPresent(DataFormats.Text));
        var files = Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop));
        Assert.Equal(["clip.gif"], files);
        Assert.Contains(DataFormats.FileDrop, data.GetFormats());

        data.SetData(DataFormats.Text, "hello");
        Assert.True(data.GetDataPresent(typeof(string)));
        Assert.Equal("hello", data.GetData(typeof(string)) as string);
    }

    [Fact]
    public void Data_object_typed_helpers_match_encoder_clipboard_shapes()
    {
        var byFile = new DataObject();
        byFile.SetFileDropList(new StringCollection { "out.gif" });
        Assert.True(byFile.ContainsFileDropList());
        Assert.Equal("out.gif", byFile.GetFileDropList()[0]);
        Assert.True(byFile.GetDataPresent(DataFormats.FileDrop));

        var byImage = new DataObject();
        byImage.SetImage(new BitmapImage());
        Assert.True(byImage.ContainsImage());
        Assert.NotNull(byImage.GetImage());

        var byText = new DataObject();
        byText.SetText("C:\\vid\\clip.gif", TextDataFormat.Text);
        Assert.True(byText.ContainsText());
        Assert.Equal("C:\\vid\\clip.gif", byText.GetText());
        Assert.Equal("C:\\vid\\clip.gif", byText.GetData(DataFormats.UnicodeText) as string);
    }

    [Fact]
    public void Pasting_veto_runs_converted_validation_before_the_command()
    {
        static bool IsTextAllowed(string? text) =>
            !string.IsNullOrEmpty(text) && text.All(char.IsDigit);

        var box = new TextBox();
        string? seen = null;
        box.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler((sender, e) =>
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = e.DataObject.GetData(typeof(string)) as string;
                seen = text;
                if (!IsTextAllowed(text)) e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }));

        var blocked = new DataObjectPastingEventArgs(
            new DataObject(typeof(string), "12a"), isDragDrop: false, DataFormats.Text)
        {
            RoutedEvent = DataObject.PastingEvent,
        };
        box.RaiseEvent(blocked);
        Assert.Equal("12a", seen);
        Assert.True(blocked.CommandCancelled);

        var allowed = new DataObjectPastingEventArgs(
            new DataObject(typeof(string), "42"), isDragDrop: false, DataFormats.Text)
        {
            RoutedEvent = DataObject.PastingEvent,
        };
        box.RaiseEvent(allowed);
        Assert.Equal("42", seen);
        Assert.False(allowed.CommandCancelled);
    }

    [Fact]
    public void Drop_target_runs_the_editor_enter_and_drop_decisions()
    {
        var target = new Border { AllowDrop = true };
        DragDropEffects enterEffect = DragDropEffects.None;
        target.DragEnter += (sender, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            enterEffect = e.Effects;
        };
        string[]? dropped = null;
        DragDropKeyStates dropKeys = DragDropKeyStates.None;
        target.Drop += (sender, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] fileNames || fileNames.Length == 0)
                return;
            dropped = fileNames;
            dropKeys = e.KeyStates;
        };

        var enter = new DragEventArgs(
            new DataObject(DataFormats.FileDrop, new[] { "a.png", "b.mp4" }),
            DragDropKeyStates.ControlKey, DragDropEffects.Copy | DragDropEffects.Move);
        target.RaiseDragEvent(enter, DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent);
        Assert.Equal(DragDropEffects.Copy, enterEffect);
        Assert.Equal(DragDropEffects.Copy | DragDropEffects.Move, enter.AllowedEffects);

        var drop = new DragEventArgs(
            new DataObject(DataFormats.FileDrop, new[] { "a.png", "b.mp4" }),
            DragDropKeyStates.ControlKey, DragDropEffects.Copy);
        target.RaiseDragEvent(drop, DragDrop.PreviewDropEvent, DragDrop.DropEvent);
        Assert.Equal(["a.png", "b.mp4"], dropped);
        Assert.Equal(DragDropKeyStates.ControlKey, dropKeys);

        var noFiles = new DragEventArgs(
            new DataObject(DataFormats.Text, "nope"), DragDropKeyStates.None, DragDropEffects.Copy);
        target.RaiseDragEvent(noFiles, DragDrop.PreviewDragEnterEvent, DragDrop.DragEnterEvent);
        Assert.Equal(DragDropEffects.None, enterEffect);
    }

    [Fact]
    public void Drop_routed_handlers_bubble_to_the_parent()
    {
        var parent = new StackPanel();
        var child = new Border { AllowDrop = true };
        parent.AddChild(child);
        var window = new Window { Content = parent };

        var bubbled = 0;
        parent.AddHandler(DragDrop.DropEvent, new DragEventHandler((sender, e) => bubbled++));
        var drop = new DragEventArgs(
            new DataObject(DataFormats.FileDrop, new[] { "a.gif" }),
            DragDropKeyStates.None, DragDropEffects.Copy);
        child.RaiseDragEvent(drop, DragDrop.PreviewDropEvent, DragDrop.DropEvent);
        Assert.Equal(1, bubbled);
    }

    [Fact]
    public void Drag_source_has_no_modal_loop_in_the_browser()
    {
        var source = new Border();
        var data = new DataObject(DataFormats.FileDrop, new[] { "out.gif" });
        var error = Assert.Throws<NotSupportedException>(() =>
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy));
        Assert.Contains("DoDragDrop", error.Message);
    }

    [Fact]
    public void Clipboard_data_object_bridge_carries_encoder_payloads()
    {
        var provider = Clipboard.Provider;
        try
        {
            var data = new DataObject();
            data.SetText("clip-path", TextDataFormat.Text);
            Clipboard.SetDataObject(data, true);
            Assert.True(Clipboard.ContainsText());
            Assert.Equal("clip-path", Clipboard.GetText());

            var view = Clipboard.GetDataObject();
            Assert.True(view.GetDataPresent(DataFormats.UnicodeText));
        }
        finally
        {
            Clipboard.Provider = provider;
            Clipboard.Clear();
        }
    }

    [Fact]
    public void Loader_parses_allow_drop_and_drop_wires_like_the_editor()
    {
        var result = XamlLoader.Load("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid AllowDrop="True" Drop="Control_Drop" DragEnter="Control_DragEnter">
                <Border/>
              </Grid>
            </Window>
            """);
        var grid = Assert.IsType<Grid>(Assert.IsType<Window>(result.Root).Content);
        Assert.True(grid.AllowDrop);
        Assert.Contains(result.EventWires, w => w.EventName == "Drop" && w.HandlerName == "Control_Drop");
        Assert.Contains(result.EventWires, w => w.EventName == "DragEnter" && w.HandlerName == "Control_DragEnter");
    }
}
