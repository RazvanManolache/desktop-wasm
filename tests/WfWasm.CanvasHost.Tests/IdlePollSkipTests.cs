using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsPanel = System.Windows.Forms.Panel;

namespace WfWasm.CanvasHost.Tests;

// RVW3293-NEXT: both scroll-watch polls (grid + RTE) feed only NoteScroll,
// which a dirty frame consumes via TakeScrollShift. On a provably-idle
// frame (empty dirty map, not global) the polls are pure cost — per-call
// reflection over every subscribed control — with no consumer: a Value
// delta found on an idle frame cannot paint anything (nothing is dirty),
// and the displacement is still fully observed on the next dirty frame.
public sealed class IdlePollSkipTests
{
    private sealed class FakeSource : RichTextScrollWatch.IScrollSource
    {
        public readonly Dictionary<FormsControl, RichTextScrollWatch.ScrollSnapshot> States = new();
        public int StateReads;

        public bool TryResolveEditor(FormsControl target, out FormsControl? editor)
        {
            for (FormsControl? c = target; c is not null; c = c.Parent)
            {
                if (States.ContainsKey(c))
                {
                    editor = c;
                    return true;
                }
            }
            editor = null;
            return false;
        }

        public bool TryGetState(FormsControl editor, out RichTextScrollWatch.ScrollSnapshot state)
        {
            StateReads++;
            return States.TryGetValue(editor, out state);
        }
    }

    private sealed class CountingPanel : FormsPanel
    {
        public int Paints;
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => Paints++;
    }

    private static readonly object Doc = new();

    private static RichTextScrollWatch.ScrollSnapshot Snap(int vValue) =>
        new(0, vValue, 0, 2000, 600, 0, 2000, 600, Doc, 0, 60, 40, 1.0, 1.0);

    private static FieldInfo WatchField() =>
        typeof(WinFormsAppHost).GetField("s_rteScrollWatch",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void Idle_Frames_Skip_ScrollWatch_Polls_Without_Losing_Scroll()
    {
        // The swapped watch is process-global: without this gate, other
        // tests' hosts polling on their own dirty frames would read the
        // fake source and pollute StateReads. Production takes the same
        // gate around its two poll calls, so holders simply wait out this
        // test (~100ms) instead of interleaving.
        lock (WinFormsAppHost.ScrollPollGate)
        {
        var field = WatchField();
        Assert.NotNull(field);
        object? saved = field.GetValue(null);
        var fake = new FakeSource();
        field.SetValue(null, new RichTextScrollWatch(fake));
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Smoke);
            var form = host.GetOrCreateForm(AppScene.Smoke);
            Assert.NotNull(form);
            var panel = new CountingPanel { Left = 120, Top = 4, Width = 60, Height = 40 };
            form.Controls.Add(panel);
            try
            {
                host.Paint(new RecordingRenderer()); // prime (global frame)
                var watch = (RichTextScrollWatch)field.GetValue(null)!;
                fake.States[panel] = Snap(0);
                Assert.True(watch.Ensure(panel));
                fake.StateReads = 0; // subscription baseline read is not a poll

                // Provably-idle frames: nothing marked, nothing global.
                host.Paint(new RecordingRenderer());
                host.Paint(new RecordingRenderer());
                Assert.Equal(0, fake.StateReads);

                // Scrollbar moves while frames stay idle: displacement must
                // not be lost, only deferred to the next dirty frame.
                fake.States[panel] = Snap(10);
                host.Paint(new RecordingRenderer());
                Assert.Equal(0, fake.StateReads);

                // A dirty frame observes the FULL accumulated displacement.
                // (A concurrent suite host may drain our mark first and park
                // at the poll gate; retry re-marks until our own frame wins.
                // Each attempt is self-consistent, so retries are exact.)
                // A dirty frame still polls (reads advance) and repaints the
                // marked control. The full -25 displacement is observed in
                // that single poll by construction: the skipped idle frames
                // provably never polled (reads stayed 0 above), so the watch
                // baseline is still Snap(0) when Snap(25) is read. (The note
                // itself may be consumed by the frame's own upload proof, so
                // the test observes the poll + repaint, not the take.)
                // A concurrent suite host may drain our mark first and park
                // at the poll gate; retry re-marks until our own frame wins.
                fake.States[panel] = Snap(25);
                long readsBeforeMark = fake.StateReads;
                int paintsBefore = panel.Paints;
                bool painted = false;
                for (int i = 0; i < 20 && !painted; i++)
                {
                    WasmRepaintGate.MarkDirty(panel);
                    host.Paint(new RecordingRenderer());
                    if (panel.Paints > paintsBefore && fake.StateReads > readsBeforeMark)
                        painted = true;
                }
                Assert.True(painted, $"dirty frame did not poll+repaint (readsDelta={fake.StateReads - readsBeforeMark} paintsDelta={panel.Paints - paintsBefore})");
            }
            finally
            {
                form.Controls.Remove(panel);
                panel.Dispose();
                WasmRepaintGate.DrainDirty(out _);
                WasmRepaintGate.TakeScrollShift(panel, out _, out _);
            }
        }
        finally
        {
            field.SetValue(null, saved);
        }
        }
    }
}
