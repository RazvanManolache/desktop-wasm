extern alias Fork;
extern alias WasmDraw;

using System.Text.Json;
using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;
using DesktopWasm.Wpf;
using ForkForms = Fork::System.Windows.Forms;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Browser-oracle input export: renders both guests headlessly and writes
/// the real frame payloads (WPF draw commands incl. text, WinForms texture
/// bytes) for the headless-Chromium oracle driver
/// (<c>BrowserOracle/run-oracle.py</c>). Structural invariants are asserted
/// here; pixels, console errors and screenshots are asserted in the browser.
/// </summary>
public sealed class BrowserOracleExportTests
{
    [Fact]
    public void Export_frames_for_browser_oracle()
    {
        var stamp = Environment.GetEnvironmentVariable("INTEROP_ORACLE_STAMP")
            ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var dir = System.IO.Path.Combine(
            "E:/AI/Code/DesktopWasm/artifacts/interop-oracle", stamp);
        System.IO.Directory.CreateDirectory(dir);

        // WPF half: real button text command at identity DPI.
        using var wpf = new WpfGuestSession(new Button { Content = "Hi" }, new WpfSize(800, 600));
        var wpfFrame = wpf.RenderFrame();
        var drawText = wpfFrame.Commands.OfType<DrawCommand.DrawText>().FirstOrDefault();
        Assert.NotNull(drawText);

        // WinForms half: solid-red panel at 2x DPI (40x30 layout -> 80x60 device).
        using var wf = new WinFormsGuestSession(
            new ForkForms.Panel { BackColor = WasmDraw::System.Drawing.Color.Red },
            new WpfSize(40, 30), InteropDpi.ForScale(2));
        var wfFrame = wf.RenderFrame();
        var drawImage = Assert.Single(wfFrame.Commands);
        var image = Assert.IsType<DrawCommand.DrawImage>(drawImage);
        Assert.Equal(80 * 60 * 4, image.Rgba.Length);

        var payload = new
        {
            stamp,
            wpf = new
            {
                width = wpfFrame.Width,
                height = wpfFrame.Height,
                text = drawText.Text,
                fontCss = drawText.FontCss,
                x = drawText.X,
                y = drawText.Y,
                rgba = drawText.Rgba,
            },
            winforms = new
            {
                layoutWidth = 40,
                layoutHeight = 30,
                width = wfFrame.Width,
                height = wfFrame.Height,
                dpi = 2.0,
                rgbaBase64 = Convert.ToBase64String(image.Rgba),
            },
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "frames.json"), json);
    }
}

