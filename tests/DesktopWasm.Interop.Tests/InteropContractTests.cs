using DesktopWasm.Core;
using DesktopWasm.Graphics;
using DesktopWasm.Interop;

namespace DesktopWasm.Interop.Tests;

/// <summary>
/// Engine-neutral interop contracts: DPI mapping, pointer translation with
/// clipping, and the shared frame exchange. Failing-first for the new
/// DesktopWasm.Interop surface contracts.
/// </summary>
public sealed class InteropContractTests
{
    [Fact]
    public void Dpi_identity_roundtrips()
    {
        var dpi = InteropDpi.Identity;
        Assert.Equal(1, dpi.ScaleX);
        Assert.Equal(1, dpi.ScaleY);
        Assert.Equal(400, dpi.ToDeviceX(400));
        Assert.Equal(300, dpi.ToLayoutY(300));
    }

    [Fact]
    public void Dpi_nonuniform_scales_each_axis()
    {
        var dpi = InteropDpi.ForScale(2, 1.5);
        Assert.Equal(800, dpi.ToDeviceX(400));
        Assert.Equal(450, dpi.ToDeviceY(300));
        Assert.Equal(400, dpi.ToLayoutX(800));
        Assert.Equal(300, dpi.ToLayoutY(450));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Dpi_rejects_nonpositive_or_nonfinite_scale(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InteropDpi.ForScale(scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteropDpi.ForScale(1, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => InteropDpi.ForScale(scale, 1));
    }

    [Fact]
    public void Dpi_rejects_nonfinite_coordinates()
    {
        var dpi = InteropDpi.ForScale(2);
        Assert.Throws<ArgumentException>(() => dpi.ToDeviceX(double.NaN));
        Assert.Throws<ArgumentException>(() => dpi.ToLayoutY(double.PositiveInfinity));
    }

    [Fact]
    public void Pointer_maps_device_to_layout_with_dpi()
    {
        var dpi = InteropDpi.ForScale(2);
        var pointer = new HostPointerEvent(DeviceX: 400, DeviceY: 300,
            Button: WasmPointerButton.Left, Kind: WasmPointerKind.Down);
        var layout = pointer.ToLayout(dpi);
        Assert.Equal(200, layout.X);
        Assert.Equal(150, layout.Y);
    }

    [Fact]
    public void Pointer_inside_includes_edges_excludes_outside()
    {
        var dpi = InteropDpi.Identity;
        // Host is 800x600 layout px; device == layout at scale 1.
        Assert.True(new HostPointerEvent(0, 0, WasmPointerButton.Left, WasmPointerKind.Down).IsInside(dpi, 800, 600));
        Assert.True(new HostPointerEvent(800, 600, WasmPointerButton.Left, WasmPointerKind.Down).IsInside(dpi, 800, 600));
        Assert.False(new HostPointerEvent(801, 300, WasmPointerButton.Left, WasmPointerKind.Down).IsInside(dpi, 800, 600));
        Assert.False(new HostPointerEvent(-1, 300, WasmPointerButton.Left, WasmPointerKind.Down).IsInside(dpi, 800, 600));
        Assert.False(new HostPointerEvent(400, 601, WasmPointerButton.Left, WasmPointerKind.Down).IsInside(dpi, 800, 600));
    }

    [Fact]
    public void Pointer_rejects_nonfinite_device_coordinates()
    {
        var bad = new HostPointerEvent(double.NaN, 0, WasmPointerButton.Left, WasmPointerKind.Down);
        Assert.Throws<ArgumentException>(() => bad.ToLayout(InteropDpi.Identity));
        Assert.Throws<ArgumentException>(() => bad.IsInside(InteropDpi.Identity, 800, 600));
    }

    [Fact]
    public void Key_converts_to_wasm_args_preserving_modifiers()
    {
        var key = new HostKeyEvent("A", IsDown: true, Ctrl: true);
        var window = new WasmWindowHandle(7);
        var args = key.ToWasmArgs(window);
        Assert.Equal(window, args.Window);
        Assert.Equal("A", args.Key);
        Assert.True(args.IsDown);
        Assert.True(args.Ctrl);
        Assert.False(args.Shift);
    }

    [Fact]
    public void Frame_guards_size_and_preserves_commands()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HostedFrame(0, 600, InteropDpi.Identity, Array.Empty<DrawCommand>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HostedFrame(800, -1, InteropDpi.Identity, Array.Empty<DrawCommand>()));

        IReadOnlyList<DrawCommand> commands = [new DrawCommand.FillRect(1, 2, 3, 4, 0xFF0000FF)];
        var frame = new HostedFrame(800, 600, InteropDpi.Identity, commands);
        Assert.Same(commands, frame.Commands);
        Assert.Equal(800, frame.Width);
        Assert.Equal(600, frame.Height);
    }
}
