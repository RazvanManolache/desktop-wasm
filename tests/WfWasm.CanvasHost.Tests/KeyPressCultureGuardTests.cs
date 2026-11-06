// RT-C1-KEYPRESS-1512-d5n8: runtime half of the joint c1 item.
// Served: printable char into the docking canvas throws
// TargetInvocationException <- ArgumentOutOfRangeException culture ('0')
// from a vendor/app OnKeyPress chain resolving culture id 0. The runtime
// owns the catch site + reflection invoke (WinFormsAppHost.RaiseKey):
// a narrow culture-signature guard retries once with invariant cultures
// pinned and reports the ORIGINAL error when the retry also fails
// (no swallow, no loop). These tests model a handler that resolves 0
// from a non-invariant ambient (the only shape an invoke-site guard
// can address) plus the always-failing fallback pin.
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;

public sealed class KeyPressCultureGuardTests
{
    private sealed class AmbientCultureCanvas : System.Windows.Forms.Button
    {
        public bool PressedOk;
        protected override void OnKeyPress(System.Windows.Forms.KeyPressEventArgs e)
        {
            if (System.Globalization.CultureInfo.CurrentCulture.LCID
                != System.Globalization.CultureInfo.InvariantCulture.LCID)
                throw new System.ArgumentOutOfRangeException(
                    "culture", "culture ('0') must be a non-negative and non-zero value. (Parameter 'culture')");
            PressedOk = true;
        }
    }

    private sealed class AlwaysFailingCanvas : System.Windows.Forms.Button
    {
        protected override void OnKeyPress(System.Windows.Forms.KeyPressEventArgs e)
        {
            throw new System.ArgumentOutOfRangeException(
                "culture", "culture ('0') must be a non-negative and non-zero value. (Parameter 'culture')");
        }
    }

    private static (WinFormsAppHost host, System.Windows.Forms.Form form, T canvas) FocusCanvas<T>()
        where T : System.Windows.Forms.Control, new()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(form);
        var canvas = new T
        {
            Location = new System.Drawing.Point(300, 10),
            Size = new System.Drawing.Size(120, 30),
        };
        form.Controls.Add(canvas);
        int cx = form.Location.X + 300 + 60;
        int cy = form.Location.Y + 30 + 10 + 15;
        host.OnMouseDown(cx, cy);
        var field = typeof(WinFormsAppHost).GetField("_focusedControl",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Same(canvas, field.GetValue(host));
        return (host, form, canvas);
    }

    private static string CaptureConsole(System.Action body)
    {
        var old = Console.Out;
        var sb = new System.Text.StringBuilder();
        System.Console.SetOut(new System.IO.StringWriter(sb));
        try
        {
            body();
        }
        finally
        {
            System.Console.SetOut(old);
        }
        return sb.ToString();
    }

    [Fact]
    public void CultureZero_Signature_Heals_With_Invariant_Retry()
    {
        // RED pre-fix: the culture-('0') failure is reported and the press
        // is lost. GREEN: one invariant-pinned retry succeeds, no error line.
        var (host, form, canvas) = FocusCanvas<AmbientCultureCanvas>();
        using (host)
        using (canvas)
        try
        {
            var savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            var savedUi = System.Threading.Thread.CurrentThread.CurrentUICulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                string log = CaptureConsole(() => host.OnKeyDown("a"));
                Assert.DoesNotContain("[WinFormsAppHost] Key press error", log);
                Assert.True(canvas.PressedOk);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = savedCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = savedUi;
            }
        }
        finally
        {
            form.Controls.Remove(canvas);
        }
    }

    [Fact]
    public void CultureZero_Signature_Retry_Failure_Reports_Original_Once()
    {
        // Fallback pin (passes pre and post): when the retry also fails the
        // ORIGINAL error is reported exactly once — no swallow, no loop.
        var (host, form, canvas) = FocusCanvas<AlwaysFailingCanvas>();
        using (host)
        using (canvas)
        try
        {
            string log = CaptureConsole(() => host.OnKeyDown("a"));
            int count = 0;
            foreach (string line in log.Split('\n'))
                if (line.StartsWith("[WinFormsAppHost] Key press error"))
                    count++;
            Assert.Equal(2, count);
        }
        finally
        {
            form.Controls.Remove(canvas);
        }
    }
}
