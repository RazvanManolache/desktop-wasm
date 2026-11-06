using DesktopWasm.BrowserHost;
using DesktopWasm.Core;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// F-drag-drop-01: managed repro of the DragDropLab paste/veto wiring
/// (WireDragDrop OnHostReady). Runs the lab's real enter/drop decisions
/// over real data objects: hover-then-drop acceptance into the session
/// store, paste allow/veto with status changes, empty/non-media drop
/// denial, and the HwndSource DPI hook lifecycle. No browser.
/// </summary>
[Collection("ApplicationState")]
[ResetAmbientState]
public sealed class DragDropLabTests
{
    [Fact]
    public void DragDrop_lab_enter_drop_veto_and_empty_denial()
    {
        var previousFs = WasmVirtualFileSystem.Current;
        var fs = new WasmVirtualFileSystem();
        fs.Mount("lab-uploads");
        WasmVirtualFileSystem.Current = fs;
        try
        {
            var demo = DemoCatalog.Find("drag-drop");
            DragDropLab.Build();
            var lab = DragDropLab.Current!;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(lab.Window, new WpfSize(800, 600));
            demo.OnHostReady?.Invoke(host);
            host.Tick(new FrameContext(1, 0));

            // Hover with text (no files) runs the enter decision: None.
            lab.Hover(new DataObject(DataFormats.Text, "dragged text"));
            Assert.Equal("Hover: no files (None).", lab.EffectReadout.Text);

            // Hover-then-drop with media files: enter Copy, drop accepted.
            lab.Hover(new DataObject(
                DataFormats.FileDrop, new[] { "lab-drops/clip.gif" }));
            Assert.Equal("Hover: files accepted (Copy).", lab.EffectReadout.Text);
            lab.AcceptFiles([
                ("clip.gif", new byte[] { 1, 2, 3 }),
                ("frame.png", new byte[] { 4, 5 }),
            ]);
            Assert.Equal(
                new byte[] { 1, 2, 3 },
                WasmVirtualFileSystem.Current.ReadFile("lab-drops/clip.gif"));

            // Paste: digits pass and move the status line, non-digits veto.
            lab.TryPaste("42");
            Assert.Equal("42", lab.PasteBox.Text);
            Assert.Equal("Pasted '42'.", lab.PasteReadout.Text);
            lab.TryPaste("12a");
            Assert.Equal("42", lab.PasteBox.Text);
            Assert.Equal(
                "Paste vetoed: '12a' is not digits-only.", lab.PasteReadout.Text);

            // Empty browser drop is denied and stores nothing.
            lab.BrowserDrop([]);
            Assert.Equal(
                "Drop ignored: the browser delivered no files.", lab.FilesReadout.Text);
            Assert.False(WasmVirtualFileSystem.Current.Exists("lab-drops/empty.bin"));

            // Non-media payload is rejected through the same drop decision.
            lab.BrowserDrop([("notes.txt", new byte[] { 9 })]);
            Assert.Equal(
                "Drop rejected: no media or project files in 1 file(s).",
                lab.FilesReadout.Text);

            // DPI hook lifecycle ran at Wire; layout geometry map published.
            Assert.StartsWith("Session DPI: ", lab.DpiReadout.Text, StringComparison.Ordinal);
            Assert.True(lab.Notes.ContainsKey("layout"));

            var frame = host.Tick(new FrameContext(2, 16.6));
            Assert.NotNull(frame.Json);
        }
        finally
        {
            WasmVirtualFileSystem.Current = previousFs;
        }
    }

    [Fact]
    public void DragDrop_lab_buttons_drive_the_same_decisions()
    {
        // The simulated buttons must run the exact code the browser bridge
        // calls: no separate imitation path between click and BrowserDrop.
        var previousFs = WasmVirtualFileSystem.Current;
        var fs = new WasmVirtualFileSystem();
        WasmVirtualFileSystem.Current = fs;
        try
        {
            var demo = DemoCatalog.Find("drag-drop");
            DragDropLab.Build();
            var lab = DragDropLab.Current!;
            var host = new WasmWpfHost(new WasmDispatcherPump(), new WasmInputRouter());
            host.AddWindow(lab.Window, new WpfSize(800, 600));
            demo.OnHostReady?.Invoke(host);
            host.Tick(new FrameContext(1, 0));

            lab.SimHoverButton.RaiseClick();
            Assert.Equal("Hover: no files (None).", lab.EffectReadout.Text);

            lab.SimDropButton.RaiseClick();
            Assert.Equal(
                "Dropped 2 file(s): clip.gif, frame.png.", lab.FilesReadout.Text);
            Assert.Equal(
                new byte[] { 4, 5 },
                WasmVirtualFileSystem.Current.ReadFile("lab-drops/frame.png"));

            lab.PasteGoodButton.RaiseClick();
            Assert.Equal("42", lab.PasteBox.Text);
            Assert.Equal("Pasted '42'.", lab.PasteReadout.Text);

            lab.PasteBadButton.RaiseClick();
            Assert.Equal("42", lab.PasteBox.Text);
            Assert.Equal(
                "Paste vetoed: '12a' is not digits-only.", lab.PasteReadout.Text);
        }
        finally
        {
            WasmVirtualFileSystem.Current = previousFs;
        }
    }
}
