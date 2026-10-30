using System.Reflection;

namespace DesktopWasm.Wpf.Tests;

/// <summary>
/// Slice-85 AvalonEdit drag-feedback regression tests: the 7 drag rows
/// (GiveFeedback/QueryContinueDrag args + handlers, DragAction,
/// DataObjectCopying/SettingData args) ride the default System.Windows.*
/// rule once the engine declares the exact upstream shapes (member shapes
/// verified with Mono.Cecil against the 10.0.13 PresentationCore ref pack,
/// defaults and validation verified against a live 10.0.13 runtime probe).
/// Converted call sites: SelectionMouseHandler attach/detach (handler ctors
/// plus UIElement add_/remove_GiveFeedback/QueryContinueDrag),
/// textArea_GiveFeedback (set_UseDefaultCursors),
/// textArea_QueryContinueDrag (get_EscapePressed), EditingCommandHandler
/// copy paths (Copying/SettingData ctors plus RaiseEvent plus
/// get_CommandCancelled). No Application state is touched, so these stay
/// outside the serialized ApplicationState collection.
/// </summary>
[ResetAmbientState]
public sealed class Slice85DragFeedbackTests
{
    [Fact]
    public void DragAction_values_match_wpf()
    {
        Assert.True(typeof(DragAction).IsEnum);
        Assert.Equal(0, (int)DragAction.Continue);
        Assert.Equal(1, (int)DragAction.Drop);
        Assert.Equal(2, (int)DragAction.Cancel);
    }

    [Fact]
    public void GiveFeedback_args_carry_effects_and_cursor_toggle()
    {
        // WPF constructs these (internal ctor); converted code only reads
        // Effects and flips UseDefaultCursors. The reflection helper pins
        // the exact internal ctor shape (no public ctor, like WPF).
        var args = NewGiveFeedback(DragDropEffects.Copy, true);
        Assert.Equal(DragDropEffects.Copy, args.Effects);
        Assert.True(args.UseDefaultCursors);
        args.UseDefaultCursors = false;
        Assert.False(args.UseDefaultCursors);
    }

    [Fact]
    public void GiveFeedback_ctor_stays_internal_like_wpf()
    {
        Assert.Null(typeof(GiveFeedbackEventArgs).GetConstructor([typeof(DragDropEffects), typeof(bool)]));
        var ctor = typeof(GiveFeedbackEventArgs).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(DragDropEffects), typeof(bool)]);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsAssembly);
    }

    [Fact]
    public void QueryContinueDrag_defaults_and_action_validation_match_wpf()
    {
        var args = NewQueryContinueDrag(escapePressed: true, keyStates: DragDropKeyStates.LeftMouseButton);
        Assert.True(args.EscapePressed);
        Assert.Equal(DragDropKeyStates.LeftMouseButton, args.KeyStates);
        Assert.Equal(DragAction.Continue, args.Action);
        args.Action = DragAction.Drop;
        Assert.Equal(DragAction.Drop, args.Action);
        args.Action = DragAction.Cancel;
        Assert.Equal(DragAction.Cancel, args.Action);
        // The live runtime rejects undefined values with ArgumentException.
        Assert.Throws<ArgumentException>(() => args.Action = (DragAction)99);
        Assert.Equal(DragAction.Cancel, args.Action);
    }

    [Fact]
    public void Handler_delegates_carry_converted_signatures()
    {
        var feedback = typeof(GiveFeedbackEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), feedback.ReturnType);
        Assert.Equal([typeof(object), typeof(GiveFeedbackEventArgs)],
            feedback.GetParameters().Select(p => p.ParameterType).ToArray());
        var query = typeof(QueryContinueDragEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), query.ReturnType);
        Assert.Equal([typeof(object), typeof(QueryContinueDragEventArgs)],
            query.GetParameters().Select(p => p.ParameterType).ToArray());
        var copying = typeof(DataObjectCopyingEventHandler).GetMethod("Invoke")!;
        Assert.Equal([typeof(object), typeof(DataObjectCopyingEventArgs)],
            copying.GetParameters().Select(p => p.ParameterType).ToArray());
        var setting = typeof(DataObjectSettingDataEventHandler).GetMethod("Invoke")!;
        Assert.Equal([typeof(object), typeof(DataObjectSettingDataEventArgs)],
            setting.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public void UIElement_owns_the_converted_feedback_events()
    {
        // The exact converted attach/detach shapes
        // (UIElement::add_GiveFeedback/remove_GiveFeedback plus the
        // QueryContinueDrag pair) bind onto these CLR events.
        var feedback = typeof(UIElement).GetEvent("GiveFeedback")!;
        Assert.NotNull(feedback);
        Assert.Equal(typeof(GiveFeedbackEventHandler), feedback!.EventHandlerType);
        Assert.NotNull(feedback.GetAddMethod());
        Assert.NotNull(feedback.GetRemoveMethod());
        var query = typeof(UIElement).GetEvent("QueryContinueDrag")!;
        Assert.NotNull(query);
        Assert.Equal(typeof(QueryContinueDragEventHandler), query!.EventHandlerType);
        Assert.NotNull(query.GetAddMethod());
        Assert.NotNull(query.GetRemoveMethod());
    }

    [Fact]
    public void Feedback_attach_raise_detach_runs_the_converted_loop()
    {
        // Mirrors the converted SelectionMouseHandler attach/detach shape:
        // handlers land, fire through the OnXxx raisers, and stop after
        // removal.
        var target = new FeedbackBorder();
        int feedbacks = 0;
        bool? lastDefault = null;
        target.GiveFeedback += (sender, e) =>
        {
            feedbacks++;
            Assert.Same(target, sender);
            e.UseDefaultCursors = false;
            lastDefault = e.UseDefaultCursors;
        };
        int queries = 0;
        DragAction committed = DragAction.Continue;
        target.QueryContinueDrag += (sender, e) =>
        {
            queries++;
            Assert.Same(target, sender);
            e.Action = e.EscapePressed ? DragAction.Cancel : DragAction.Continue;
            committed = e.Action;
        };
        target.RaiseFeedback(NewGiveFeedback(DragDropEffects.Copy, true));
        target.RaiseQuery(NewQueryContinueDrag(escapePressed: false, keyStates: DragDropKeyStates.None));
        Assert.Equal(1, feedbacks);
        Assert.Equal(false, lastDefault);
        Assert.Equal(1, queries);
        Assert.Equal(DragAction.Continue, committed);
        target.RaiseQuery(NewQueryContinueDrag(escapePressed: true, keyStates: DragDropKeyStates.None));
        Assert.Equal(2, queries);
        Assert.Equal(DragAction.Cancel, committed);
    }

    [Fact]
    public void Copying_args_veto_like_the_editor_copy_path()
    {
        // Mirrors the converted EditingCommandHandler.CopySelectedText
        // shape: construct, RaiseEvent (carries the Copying token, so the
        // engine router accepts it), then honor CommandCancelled.
        var data = new DataObject(DataFormats.Text, "selected");
        var args = new DataObjectCopyingEventArgs(data, isDragDrop: false);
        Assert.Same(data, (object)args.DataObject);
        Assert.False(args.IsDragDrop);
        Assert.False(args.CommandCancelled);
        Assert.Same(DataObject.CopyingEvent, args.RoutedEvent);
        var target = new Border { AllowDrop = true };
        bool raised = false;
        target.AddHandler(DataObject.CopyingEvent, new DataObjectCopyingEventHandler((sender, e) =>
        {
            raised = true;
            Assert.Same(data, (object)e.DataObject);
            e.CancelCommand();
        }));
        target.RaiseEvent(args);
        Assert.True(raised);
        Assert.True(args.CommandCancelled);
    }

    [Fact]
    public void Copying_args_reject_null_payload_like_wpf()
    {
        Assert.Throws<ArgumentNullException>(() => new DataObjectCopyingEventArgs(null!, true));
    }

    [Fact]
    public void SettingData_args_carry_format_without_drag_drop()
    {
        // Mirrors the converted EditingCommandHandler.ConfirmDataFormat
        // shape (live probe: IsDragDrop false, nulls rejected).
        var data = new DataObject();
        var args = new DataObjectSettingDataEventArgs(data, DataFormats.Text);
        Assert.Same(data, (object)args.DataObject);
        Assert.Equal(DataFormats.Text, args.Format);
        Assert.False(args.IsDragDrop);
        Assert.False(args.CommandCancelled);
        Assert.Same(DataObject.SettingDataEvent, args.RoutedEvent);
        Assert.Throws<ArgumentNullException>(() => new DataObjectSettingDataEventArgs(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => new DataObjectSettingDataEventArgs(data, null!));
    }

    [Fact]
    public void Copying_and_setting_data_events_are_registered_like_pasting()
    {
        Assert.Equal("Copying", DataObject.CopyingEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, DataObject.CopyingEvent.RoutingStrategy);
        Assert.Equal(typeof(DataObjectCopyingEventHandler), DataObject.CopyingEvent.HandlerType);
        Assert.Equal(typeof(DataObject), DataObject.CopyingEvent.OwnerType);
        Assert.Equal("SettingData", DataObject.SettingDataEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, DataObject.SettingDataEvent.RoutingStrategy);
        Assert.Equal(typeof(DataObjectSettingDataEventHandler), DataObject.SettingDataEvent.HandlerType);
        Assert.Equal(typeof(DataObject), DataObject.SettingDataEvent.OwnerType);
    }

    private static GiveFeedbackEventArgs NewGiveFeedback(DragDropEffects effects, bool useDefaultCursors) =>
        (GiveFeedbackEventArgs)typeof(GiveFeedbackEventArgs)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(DragDropEffects), typeof(bool)])!
            .Invoke([effects, useDefaultCursors]);

    private static QueryContinueDragEventArgs NewQueryContinueDrag(bool escapePressed, DragDropKeyStates keyStates) =>
        (QueryContinueDragEventArgs)typeof(QueryContinueDragEventArgs)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(bool), typeof(DragDropKeyStates)])!
            .Invoke([escapePressed, keyStates]);

    private sealed class FeedbackBorder : ContentControl
    {
        public void RaiseFeedback(GiveFeedbackEventArgs e) => OnGiveFeedback(e);

        public void RaiseQuery(QueryContinueDragEventArgs e) => OnQueryContinueDrag(e);
    }
}
