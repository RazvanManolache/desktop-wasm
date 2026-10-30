# DesktopWasm.Wpf roadmap

## Active continuation contract

The numbered "done" entries below record completed implementation slices.
They do **not** establish complete WPF compatibility, browser acceptance or
completion of the product. A green unit suite is a regression checkpoint.
Do not stop a continuation run merely because those historical steps are done.

Use [the current agent instructions](../../docs/wpf-agent-prompt.md) and
[the per-application reports](../../docs/application-compatibility-status.md)
to choose the next verifiable compatibility or performance improvement. Inspect
the current implementation and regenerate affected packages before treating
an older report as a missing feature. Keep the single task list in
`docs/preview-upgrade-tasks.md` current. Add the next implementation slice and
its acceptance criteria before extending the historical log.

Priorities observed on 2026-10-23 (refresh these against current evidence):

1. Close the browser-evidence gap after Steps 36–43. The latest saved browser
   run is `20261023T123121Z` (29 demos incl. a11y-events, 0 page errors,
   15/15 launcher checks): original Geometry verified on CPU canvas and WebGL,
   plus its radio-select workflow. Next: extend the same proof to the remaining
   apps as their gaps close; conversion success alone must not promote readiness.
2. Microsoft WPF Gallery converts with 0 gaps, boots with painted
   Dashboard/AllControls card icons, and survives extended browser sessions
   (2026-10-23 session-memory slice fixed the stochastic WASM-heap
   OutOfMemory: per-frame native shaping churn + per-publish image base64
   are now cached/upload-once with publish/GC telemetry; Recently-added
   scroll + 90 s idle proven on Canvas2D and WebGL, 0 page errors).
   Detail pages proven with post-input STATE since: Dashboard/BasicInput/Button/WhatsNew (slice-25), GroupBox (slice-32: GroupBox type plus textboxes/groupboxes STATE, 15/15 checks), TextBoxPage validation (slice-33: prefixed-rule xmlns tolerance plus ValidationResult.ValidResult property compat for staged binaries plus HasError textbox STATE, 17/17 checks, 0 page errors), GridPage (slice-34), ListBoxPage ItemsSource (slice-35), MenuPage (slice-36), CalendarPage (slice-37), DatePickerPage (slice-38), RichTextEditPage (slice-39: RichTextBox editor plus richtexts STATE, 19/19 checks, 0 page errors). Next: ScreenToGif prerequisites shared with geometry/drawing. Do not
   hand-build imitation screens.
3. ScreenToGif reports 4 gaps (was 310; 2026-10-26 platform-shims slice: WinForms/ACL/ProtectedData/CodeDom shims + KGySoft nested-base converter fix, 14 staged, hashes verified; full suite 1024 = 950 Wpf + 40 Convert + 34 Core, 0 failures). Remaining: 3x System.Drawing.Common absent-assembly rows + KGySoft.Drawing v8.0.0.0 resolve failure, declared GDI+ boundary with member-level evidence (LockBits/Scan0 pipelines, HDC/HBITMAP interop, codecs, metafiles; capture/frame-processing/export degrade, editor stays conversion-failed). Next: ScreenToGif template/resource clusters or Drawing-free editor paths; refresh the other targets too. Slice-31 (2026-10-26): WindowPlacement centering/clamp helper + shell-lab Screen/placement/Forms.Timer coverage (6 tests; shell-proof 21/21 STATE checks, 0 page errors); re-packaged 14 staged / 4 gaps with zero new rows, all four the declared GDI+ boundary, so no Drawing-free cluster can advance the editor. Full suite 1030 pass.
   Refresh the other targets too; shared foundational fixes may already exist.
4. Complete real template/resource/trigger and collection/editing/virtualization
   behavior where required by the selected original app. An existing shell
   such as ControlTemplate does not prove visual-tree instantiation works.
5. Profile input-to-visible response and cold/warm loading on real apps before
   changing scheduling or loading design; preserve dynamic app dependencies,
   optional diagnostics and integration of GPU rendering into actual apps.

When an item is complete, select the next independently verified defect or
unsupported workflow. If genuinely blocked, record exact evidence, try another
independent item and state what external action is required. Do not invent
busywork or equate lack of unchecked historical steps with product completion.

## Historical implementation checkpoints

- DynamicResource/hit-test (2026-10-24): ScreenToGif adorner/selection/theme slice. New `HitTest.cs`: `ToleranceType` (Absolute/Relative), `HitTestResultBehavior` (Stop/Continue), `HitTestFilterBehavior` (ContinueSkipSelfAndChildren/ContinueSkipChildren/ContinueSkipSelf/Continue/Stop with the odd WPF values), `IntersectionDetail` (NotCalculated/Empty/FullyInside/FullyContains/Intersects) — enum values verified against the WindowsDesktop ref assemblies — plus `HitTestResult` (DependencyObject VisualHit)/`PointHitTestResult`/`GeometryHitTestResult`, `HitTestParameters`/`PointHitTestParameters`/`GeometryHitTestParameters`, both callbacks, `VisualCollection` (owner parenting, duplicate/re-parent guards, nested Enumerator) and `VisualTreeHelper.HitTest` point/callback overloads walking visual children plus logical children topmost-first in reference coordinates with filter skip/stop semantics. `Visual` gains protected `AddVisualChild`/`RemoveVisualChild`/`VisualChildrenCount`/`GetVisualChild` plus protected-internal change hooks (exact WPF accessibility) for converted adorner overrides. New `DynamicResource.cs`: `MarkupExtension`, `IProvideValueTarget`, `DynamicResourceExtension` (key resolve through element/ancestor/app scopes, DP/CLR landing, live re-apply + repaint on resource change, missing keys stay null); `BindingBase.ProvideValue` attaches a live transfer for the converted DynamicResourceBinding body. `Geometry.GetFlattenedPathGeometry`/`Combine` (bezier/arc subdivision, bounds-degrade documented) + `BorderBrush` DP change callback so DP-driven brushes repaint. 16 HitTestFamilyTests + 7 DynamicResourceExtensionTests + 2 HitTestLabTests + Convert MapCoversDynamicResourceHitTestSlice green; new hittest lab + wpf-hittest.js channel + hittest_proof.py browser proof on a fresh Chromium profile (Pick: Alpha, Selected: Alpha+Beta with repaint, Theme dark/light with repaint, 104647 ink px, 0 page errors; results/hittest/; baselines/latest.json covers 37 demos, bench BENCH PASS 37 demos). Screen-to-gif re-packaged 67 -> 56 gaps with zero slice gaps left (10 staged, hashes verified). Full suite 901 (33 Convert + 834 Wpf + 34 Core) pass, node studio 14 pass. ScreenToGif still conversion-failed (next: Gallery boot chain, Fluent pack merge). Retained limits: geometry hits are bounds-based (no pixel-exact path intersection), Combine keeps union-of-outlines (no boolean path-op), HitTest skips non-UIElement visuals, converter-carrying DynamicResourceBinding re-runs only on direct sets until ScreenToGif.Util converts.
- Text/font/DPI (2026-10-24): ScreenToGif text rendering slice. New `TextFontDpi.cs`: `Typeface` (family/style/weight/stretch resolved through the shared TextShaper face cache, measured XHeight/CapsHeight/underline/strikeout, TryGetGlyphTypeface) + `GlyphTypeface` (character map measured off native lookups, advances in em units, em-fraction metrics, StyleSimulations), `DpiScale` + `DpiChangedEventArgs/Handler` (session scale via VisualTreeHelper.GetDpi/SetRootDpi, Window.DpiChanged), `NumberSubstitution` (+CultureSource/Method enums with real native-digit mapping), `TextFormattingMode` (Display snaps to whole pixels), `TextDecoration` (: Freezable with DP fields) + `TextDecorationCollection` (: List with Clone/Add/TryRemove) + `TextDecorations` stock frozen sets + Parse, `DesignerProperties`, `SystemFonts` (Segoe UI 12px session defaults + *Key), `DependencyPropertyDescriptor` (FromProperty/FromName onto registration metadata) + WpfTypeMap override for the non-Windows namespace; WPF-shaped `FormattedText` replaces the legacy fontCss one (6 ctor overloads, PixelsPerDip scaling, MaxTextWidth ellipsis, digit substitution, BuildGeometry real glyph outlines via native text path scaled to DIPs with measured-rect fallback, BuildHighlightGeometry measured ranges). Engine edits: Geometry.Empty/Bounds(+Transform)/Transform/GetRenderBounds, RectangleGeometry ctors, DrawingContext.DrawGeometry(Brush,Pen,Geometry), TextBlock.TextDecorations + ink paint + XAML/StyleValueConverter parse, Window.DpiChanged + VisualTreeHelper.GetDpi/SetRootDpi. 20 TextFontDpiTests + text-lab wiring test + Convert MapCoversTextFontDpiSlice green; new text lab + wpf-text.js channel + text_proof.py browser proof on a fresh Chromium profile (Underline/Strikethrough/None cycle with +171/+124 differential decoration-band ink, FormattedText outline 877 ink px, DPI 96->192->96, 0 page errors; results/text/; baselines/latest.json covers 35 demos, bench run 20261024T022132Z). Screen-to-gif re-packaged 87 -> 71 gaps with zero slice gaps left (10 staged, hashes verified). Full suite 864 (799 Wpf + 31 Convert + 34 Core) pass, node studio 14 pass. ScreenToGif still conversion-failed (next: template cluster DataTemplate/FrameworkTemplate/DataTemplateSelector). Retained limits: single-line layout only (MaxTextHeight/MaxLineCount retained, no wrapping), no FontEmbeddingRight/Symbol/vertical bearings (no Skia source), no multi-range Set* span storage, no OS font-metric source (session defaults).
- Animation clocks (2026-10-24): ScreenToGif HideableTabControl show/hide slice. New `AnimationClocks.cs`: `EasingMode`, `EasingFunctionBase : Freezable, IEasingFunction` (mode-adjusted Ease around abstract EaseInCore), `PowerEase` (Power defaults 2), `HandoffBehavior`, `KeyTimeType` + `KeyTime` (TimeSpan/Percent/Uniform/Paced with Resolve; Paced fails precisely), `ObjectKeyFrame` (abstract : Freezable)/`DiscreteObjectKeyFrame` (sealed, original ctor shapes)/`ObjectKeyFrameCollection`, `AnimationTimeline : Timeline` (abstract TargetPropertyType, virtual GetCurrentValue), `AnimationClock` (CurrentTime/Progress/Timeline + internal iteration duration), `ColorAnimationBase`/`ColorAnimation` (From/To/By/EasingFunction, original ctors, typed + object GetCurrentValue with byte-lerp), `ThicknessAnimation` (component lerp), `ObjectAnimationUsingKeyFrames` (greatest-key-time-wins evaluation, Automatic natural duration). `Timeline : Freezable` (+ CreateInstanceCore, shared accel/decel curve), `DoubleAnimation : AnimationTimeline` (original from/to/duration ctors, TargetPropertyType, typed/object GetCurrentValue), `WpfAnimationClock` generalized timing + BeginAnimation registry (SnapshotAndReplace restores base, Compose snapshots current; null removes; prohibited/type-mismatch/Paced fail fast), `UIElement.BeginAnimation` overloads, `Visibility`/`Opacity`/`Margin` DP-backed (MarginProperty/OpacityProperty/VisibilityProperty). 11 AnimationClocksTests (incl. animation-lab show/hide round trip with exact interpolated values) + Convert MapCoversAnimationClocksSlice green; new animation lab + wpf-animation.js channel + animation_proof.py browser proof on a fresh Chromium profile (Hide 100->99->87->63->0 monotonic + Collapsed repaint, Show 0->2->8->65->100 + Visible, afterShow hash == before, 117080 ink px, 0 page errors; results/animation/); baselines/latest.json covers 34 demos, bench BENCH PASS 34 demos. Screen-to-gif re-packaged 99 -> 87 gaps with zero slice gaps left (10 staged, hashes verified). Full suite 843 (779 Wpf + 30 Convert + 34 Core) pass, node studio 14 pass. ScreenToGif still conversion-failed (next: text/font/DPI cluster). Retained limits: no template inflation (HideableTabControl parts dormant), Opacity values stored without translucency compositing (XAML still loads only 1.0), dotted/indirect Storyboard target paths stay precise boundaries, no DoubleAnimationUsingKeyFrames/PointAnimationUsingKeyFrames XAML parsing.
- Tabs/tooltips (2026-10-24): ScreenToGif ribbon/encoder/toolbar slice. New `TabProgressTooltip.cs`: `TabItem : ContentControl` (Header, two-way IsSelected through a TabPanel host), `TabControl : Selector` (SelectedContent, TabStripPlacement/Dock, header press + arrow keys, disabled skip, auto-first, content visual hosting, header/content paint), `TabPanel : Panel` (IsItemsHost, horizontal/vertical stack), `ProgressBar : RangeBase` (WPF 0..100, coercion, ValueChanged, determinate fill + indeterminate marquee, Orientation), `ToolTip : ContentControl` (Placement/offsets/StaysOpen/IsOpen/Opened/Closed, hit-test-transparent, overlay paint) + `ToolTipService` (attached ToolTip/Placement/offsets/ShowDuration over the shared bag, hover open/close, shared string cards, tip-owned placement fallback). `FrameworkElement.ToolTip` widened to object, `Panel.IsItemsHost`, XamlLoader elements/attrs/SelectionChanged+Opened/Closed wires/property elements/text, ApplicationXaml offsets, `WpfTypeMap` + `ValidateEngineTargets`; `WasmMilCore` overlay pass paints open tips. 12 TabProgressTooltipTests + Convert MapCoversTabProgressTooltipSlice green; tabs lab + wpf-tabs.js channel + tabs_proof.py browser proof on a fresh Chromium profile (Tab: 1/Profile, Progress: 50, tip open/close, 0 page errors; baselines/latest.json covers 33 demos, results 20261024T005536Z, bench BENCH PASS 33 demos). Screen-to-gif re-packaged 105 -> 99 gaps with zero slice gaps left (10 staged, hashes verified). Full suite 831 (768 Wpf + 29 Convert + 34 Core) pass, node studio 14 pass. ScreenToGif still conversion-failed (next: animation clocks). Retained limits: no template inflation (HideableTabControl parts dormant), no visual Header content, tooltip show delays, no screen-edge clamping.
- Primitives (2026-10-24): ScreenToGif drag/spinner/menu slice. New `Primitives.cs`: `Thumb` (DragDelta/DragStarted/DragCompleted routed tokens + CLR events through the real pointer path with Mouse.Capture, IsDragging DP, CancelDrag), drag args/handlers with original ctor shapes, `RepeatButton : Button` (Delay=500/Interval=33 press-and-hold repeat via DispatcherTimer; quick press fires once on release, cancel silent), `Popup` (Child/Placement/Target/Rectangle/offsets/StaysOpen/AllowsTransparency, Opened/Closed, Bottom-default anchor math, inline paint + hit-test + overlay routing, light-dismiss through WpfInputManager, Escape close, focus in/out), `UIElement.Cursor` retained; XAML loader parses the elements/attributes/wires/single-child content; 12 PrimitivesTests + demo-wiring test (exact readout strings) + Convert MapCoversPrimitivesSlice green; screen-to-gif re-packaged 113 -> 105 gaps with zero slice gaps left (10 staged, hashes verified). New primitives demo + wpf-primitives.js status channel: browser proof asserts post-input state strings (drag/repeat/hold-stop/popup open/light-dismiss) with 0 page errors; baselines/latest.json covers 32 demos. Full suite 818 + node studio 14 pass. ScreenToGif still conversion-failed (next: TabControl/ProgressBar/ToolTip). Retained limits: template shells still do not inflate (SplitButton/UpDown template FindName paths stay dormant), PopupAnimation/StaysOpen-on-ContextMenu unimplemented, no screen-edge clamping, no overlay post-pass.

- Adorner/binding-ops (2026-10-24): ScreenToGif crop-overlay/editor slice.
  New `Adorner.cs`: `Decorator` (single-child host, Child/Children in sync),
  `Adorner` (abstract; MeasureCore/ArrangeCore route through Size-typed
  MeasureOverride/ArrangeOverride so converted `protected override`s bind),
  `AdornerLayer` (Add/Remove, GetAdornerLayer over ancestors + the
  AdornerDecorator host, arrange over adorned RenderRect, paints after
  content), `AdornerDecorator` (owned layer, XAML tags added). New binding
  surface in `DataBinding.cs`/`UiElement.cs`: `PropertyPath` (string + DP
  ctors, PathParameters, implicit conversions; `Binding.Path` is now
  PropertyPath), `BindingExpressionBase` (BindingExpression rebased with
  override UpdateTarget/UpdateSource/Detach), `BindingOperations`
  (DP-named SetBinding/ClearBinding/ClearAllBindings/GetBinding/
  GetBindingExpression(Base)/IsDataBound), FrameworkElement
  GetBindingExpression(dp)/GetBindingExpressionBase(dp)/DataContextProperty/
  DataContextChanged. 12 AdornerBindingOpsTests + Convert
  MapCoversAdornerBindingOpsSlice green; screen-to-gif re-packaged
  120 -> 113 gaps with zero slice gaps left (10 staged, hashes verified).
  Engine-native proof: adorner FillRect after/over the adorned rect.
  Retained limits: converted adorner subclasses still need Thumb/visual-tree
  (next slice), DP-direct transfers without CLR wrappers, LogicalChildren
  stays a list (converted IEnumerator overrides will not load).
- ScrollChanged events (2026-10-23): ScreenToGif ZoomBox/ImageViewer/ScrollSynchronizer slice.
  New `ScrollChangedEventArgs` (internal Vector/Size ctor like WPF; 12 public
  offset/extent/viewport + change getters) + `ScrollChangedEventHandler` in
  `RoutedEvents.cs`; `ScrollViewer` gains `ScrollChangedEvent` (Bubble), a CLR
  event over AddHandler/RemoveHandler, `OnScrollChanged`, static
  Add/RemoveScrollChangedHandler, the missing `ScrollToVerticalOffset` twin,
  and snapshot-based firing from the offset setters plus measure/arrange
  (offset/extent/viewport transitions with correct args; total no-ops stay
  silent so nested ScrollTo* calls from handlers converge). XAML loader wires
  `ScrollChanged="..."` on ScrollViewer. 11 ScrollChangedTests (ZoomBox
  extent-guard, synchronizer offset-sync, original delegate-shape binding,
  bubble, XAML wire) + scroll-demo readout test + Convert
  MapCoversScrollChangedSlice green; screen-to-gif re-packaged 122 -> 120 gaps
  with zero slice gaps left (10 staged, hashes verified). Scroll demo readout
  is driven solely by ScrollChanged: browser proof on a fresh Chrome profile
  shows wheel scrolling to rows 7..13 with readout "Offset: 240 (Δ 240)",
  0 page errors (results/scroll/). Full suite 792 (26+732+34) + node studio
  14 pass. ScreenToGif still conversion-failed (next: adorner/binding
  leftovers).
- Drag-drop/data-object + HwndSource (2026-10-23): ScreenToGif editor/clipboard/chrome slice.
  New `DragDrop.cs`: `DragDropEffects` (verified None=0/Copy=1/Move=2/Link=4/Scroll=min/All=-2147483645),
  `DragDropKeyStates` (verified values), `DataFormats` (verified names), `IDataObject`,
  `DataObject` (format store with text-family auto-conversion, typed text/image/file-drop helpers),
  `DataObjectEventArgs` (CancelCommand) + `DataObjectPastingEventArgs/Handler` (`PastingEvent` token,
  Add/RemovePastingHandler), `DragEventArgs/Handler`, `DragDrop` (8 target-side routed events,
  Add/Remove handlers; `DoDragDrop` is a precise boundary: no OLE modal loop in the browser).
  `UIElement` gains `AllowDrop`, 8 drag CLR events + virtuals, and `RaiseDragEvent` (preview CLR +
  tunneled preview, then bubble CLR + bubbled bubble unless handled); XAML loader parses
  `AllowDrop` and Drop/DragEnter/DragOver/DragLeave (+Preview) wires. `Clipboard.SetDataObject/
  GetDataObject` now bridge text/image/file-drop payloads. New `HwndInterop.cs`: `HwndSourceParameters`
  (verified shape), `HwndSourceHook`, `HwndSource` (session CompositionTarget at device scale,
  settable RootVisual, retained hooks, Handle boundary), `HwndTarget`/`CompositionTarget`
  (session scale, default 1.0 = 96 dpi), `WindowInteropHelper` (Handle boundary), `Imaging`
  (HBitmap/HIcon/MemorySection boundaries), `MSG` (verified property shape); `PresentationSource`
  gains CompositionTarget + FromVisual(Visual) so converted DPI fallbacks bind. 10 DragDropTests +
  7 HwndInteropTests + demo wiring test green; screen-to-gif re-packaged 145 -> 122 gaps with zero
  slice gaps left (23 slice-attributed; ScreenToGif.Native stages, 10 staged, hashes verified).
  New engine-native drag-drop demo + `wpf-dragdrop.js` HTML5 bridge: browser proof on a fresh
  Chromium profile shows simulated media drop (Copy + 2 files), text hover (None), paste veto/accept,
  session DPI readout, and a real File through the drop listener landing in the routed Drop
  (16/16 checks, 185752 ink px, 0 page errors; results/dragdrop/; baselines/latest.json covers
  31 demos, results 20261023T230818Z). Full suite 779 (25+720+34) + node studio 14 pass.
  Retained limits: no OLE drag-source loop (DoDragDrop throws), no HWND/GDI handles, hooks never
  fire from the host (no Win32 pump), no GiveFeedback/QueryContinueDrag/Copying/SettingData
  (unreferenced), drops without bytes are ignored, never fabricated. ScreenToGif still
  conversion-failed (next: ScrollChangedEventArgs/Handler).
- Ink/stylus + FlowDocument (2026-10-23): ScreenToGif board/document slice.
  New `Ink.cs`: StylusTip (verified Rectangle=0/Ellipse=1), ApplicationGesture
  (verified values), StylusShape/Rectangle/Ellipse nibs, DrawingAttributes
  (verified defaults black/~2px/ellipse, clamp, Clone, AttributeChanged, bag),
  StylusPoint/Collection, Stroke (nib-expanded bounds, spine geometry,
  HitTest), StrokeCollection (batch deltas, Replace/Clone/GetBounds),
  InkCanvasEditingMode (verified None=0..EraseByStroke=6), InkCanvas (real
  pointer capture, stroke commit + StrokeCollected, erase-by-point/stroke,
  selection, gestures retained, EditingMode DPs), stylus-button
  route (barrel press from the non-left pointer button, tunneling + bubble).
  New `Documents.cs`: FlowDirection, FontStretch/es, FrameworkContentElement,
  TextElement (font/ink DPs + parent-chain inheritance + statics),
  Inline/Block (+siblings), TextElementCollection<T> (+Inline/BlockCollection),
  Paragraph (word-wrap layout, per-run paint, link rects), FlowDocument
  (Blocks/PagePadding/stacked layout), FlowDocumentScrollViewer (Document,
  verified zoom defaults 100/80/200/+10, scrollbar rules). Span rebased onto
  Inline (TextBlock folding preserved); Run gains string ctors + TextProperty;
  Hyperlink gains Inlines + Inline ctor + paragraph hit-testing; XAML loader
  parses the new elements/attributes/wires. 29 InkTests + 12 FlowDocumentTests
  + Convert MapCoversInkDocumentSlice green; screen-to-gif re-packaged
  202 -> 145 gaps (57 slice-attributed, zero left; ScreenToGif.Domain stages,
  9 staged). New ink-doc demo: browser proof drag->Strokes:1 with a painted
  stroke plus WebGL ink 2832, 0 page errors (shots ink-doc/, baselines cover
  30 demos/apps). The drag misses exposed a real PositionRelativeTo ancestor
  double-subtract (RenderRect is root-absolute); fixed with a nested-layout
  regression test. Retained limits: gesture recognition, ISF codec,
  clipboard verbs, adorner selection visuals, multi-column/pagination/text
  selection, Panel.Children retype, inverted-pen switch. ScreenToGif still
  conversion-failed (next: drag-drop/data-object and HwndSource).
- Shape/StreamGeometry prerequisites (2026-10-23): ScreenToGif geometry/drawing
  slice. New engine surface: `Shape` (fill/stroke/thickness/stretch/caps/join/
  miter/dash with verified WPF defaults, abstract DefiningGeometry; Path/
  Rectangle/Ellipse rebased, Line/Polyline added) + `StreamGeometry`/
  `StreamGeometryContext` (BeginFigure/LineTo/BezierTo/QuadraticBezierTo/ArcTo/
  Poly*/Close/SetClosedState over the retained figure pipeline, Freeze) +
  `FrameworkPropertyMetadataOptions` (verified values) with flags/coerce
  metadata ctors + Coerce/Validate callbacks +
  DependencyPropertyChangedEventHandler + UIPropertyMetadata (coerce+validate
  enforced on SetValue) +
  PenLineCap/Join + DashStyle/DashStyles + ColorInterpolationMode +
  DoubleCollection + PointCollection unified onto List(Point) + Poly*Segments
  + DrawingImage + value Enumerators on the figure/segment/stop/point/double
  collections + converter nested-type fallback (WpfTypeMap.MapNestedType:
  allowlist then engine RuntimeType) + XAML Line/Polyline/shape attributes.
  13 ShapeStreamGeometryTests + Convert MapCoversShapeStreamGeometrySlice +
  RewriteMapsNestedCollectionEnumerators green; screen-to-gif re-packaged
  237 -> 202 gaps (24 slice-attributed incl. every KGySoft.Drawing.Wpf gap so
  it now stages; 11 more from the current map on pre-existing engine cover;
  hashes verified). Engine-native proof: Arrow-pattern DrawGeometry op plus
  triangle fill pixels on both renderers. Retained limits: Stretch parsed not
  scaled (needs a geometry-transform op), caps/joins/dashes retained not
  stroked, rounded-rect DefiningGeometry is square, DrawingImage rasterization
  stays a boundary. ScreenToGif still conversion-failed (next: Ink/stylus +
  FlowDocument clusters, then drag-drop/data-object and HwndSource).
- Gallery card icons (2026-10-23): bound-image slice. `DataBinding.ConvertValue`
  gains ImageSourceConverter parity (Uri/string to `ImageSource` becomes a lazy
  `BitmapImage`); previously the `Uri` landing threw inside the binding pump and
  `Image.Source` stayed null, so template-bound card icons never emitted
  `DrawImage`. 4 `GalleryCardImageTests` (incl. all 38 staged-gallery cards and
  frame op-7 bytes) green; browser proof on fresh Chromium profiles: Dashboard
  and All Controls icons paint on Canvas2D and WebGL with 0 page errors
  (shots `gallery-cards-dashboard/allcontrols.png`). Full suite 706 pass, node
  studio 14 pass. Manifests/converter untouched (no re-package). Known open
  boundary: extended gallery browser sessions hit a pre-existing stochastic
  WASM-heap OutOfMemory (also seen pre-fix, both renderers); Recently-added
  scroll and long-dwell proof stay open.
- Automation + preferences (2026-10-23): gallery final-gaps slice. New `Automation.cs`:
  `AutomationPeer` (announcement channel: NotificationRaised event + LastRaised record) +
  `UIElementAutomationPeer` (CreatePeerForElement/null-in-null-out, Owner, GetName from
  AutomationProperties.Name, null default) + `AutomationNotificationKind` (Other=4, verified
  against UIAutomationTypes) + `AutomationNotificationProcessing` + `AutomationHeadingLevel`
  + `AutomationProperties` (Name/HelpText/HeadingLevel over the attached store, loader
  attribute + property-element support) + `SystemEvents.cs` (`Microsoft.Win32`: SystemEvents
  with host-driven RaiseUserPreferenceChanged, UserPreferenceChangedEventArgs/Category with
  verified numerics) + `EventLog.cs` (`System.Diagnostics`: EventLog/EventInstance/
  EventLogEntryType with verified numerics/shapes; construction retained, writes a precise
  NotSupported boundary since the Hosting provider is Windows-only and the framework payload
  closure does not ship for the browser) + `WpfTypeMap` assemblies/overrides/probes.
  8 AutomationTests + 3 SystemEventsTests + 3 EventLogTests + 3 A11yEventsTests + Convert
  MapCoversAutomationSystemEventsSlice green; gallery re-packaged 6 -> 0 gaps (29 staged,
  hashes verified, entry present) and now converts; new a11y-events demo proves copy
  announcement into a DOM aria-live region plus theme-change repaint in the browser with
  0 page errors; baselines/latest.json covers 29 demos (results 20261023T123121Z). Full suite
  610+19+30 pass, node studio 14 pass. Retained limits: no OS automation tree (announcements
  only), matchMedia->SystemEvents host wiring not yet attached, no Windows Event Log sink,
  FrameworkElementAutomationPeer and other control peers stay gaps, gallery boot blocked on
  the Fluent theme pack merge + DynamicResource/BasedOn/TemplateBinding app XAML (next).
- Pack streams + custom chrome (2026-10-23): gallery ControlExample/MainWindow slice. New `StreamResourceInfo`
  (stream + content type, extension-mapped) + `Application.GetResourceStream/GetContentStream` resolving the
  pack store then converted-assembly manifest/`.g.resources` bytes (null when absent, the Gallery null pattern;
  remote fetch a precise boundary) + pack:// scheme registration (GenericAuthority, like WPF, so converted
  `new Uri("pack://application:,,,/...")` parses) + `WindowChrome` (`Freezable`: CaptionHeight/CornerRadius/
  GlassFrameThickness/ResizeBorderThickness/NonClientFrameEdges/UseAeroCaptionButtons, attach/get per Window,
  IsHitTestVisibleInChrome attached) + caption-band hit gating in the shared route (marked subtrees hit, the
  Window absorbs the rest) + glass/caption/edge retained paint with content untouched + XAML attached-attribute
  and style-setter wiring (`WindowChrome.IsHitTestVisibleInChrome`). 18 ChromePackTests + demo wiring test +
  Convert MapCoversChromePackSlice green; gallery re-packaged 9 -> 6 gaps (28 staged, input hashes verified);
  new engine-native chrome-pack demo proves caption click routing and glass/caption pixels in the browser with
  0 page errors; baselines/latest.json covers 28 demos. Retained limits: no caption drag/DWM blur/system menu,
  no corner-clip rendering, no BAML attached-property ids, GetContentStream shares the pack stores (no separate
  loose-content store), Clone shares the attachment set (documented v1 limit). Gallery still
  conversion-failed (next: automation peers + SystemEvents/EventLog).
- Navigation events (2026-10-23): gallery MainWindow/RootContentFrame_Navigated slice. New `NavigationEventArgs`
  (Content/Uri/ExtraData over EventArgs, so converted (object, NavigationEventArgs) handlers bind) +
  `NavigatedEventHandler`; `Frame.Navigate(object)` raises `Navigated` (content args, null Uri) before
  `ContentRendered`, per-instance so sessions isolate; failed navigates raise neither; `Navigated="..."` XAML wire
  on Frame; `WpfTypeMap` validation covers both names (default rule). 8 NavigationEventTests + Convert
  MapCoversNavigationEventsSlice green; validation-links demo readout now driven solely by `Navigated` (Gallery
  pattern); gallery re-packaged 11 -> 9 gaps (28 staged assemblies); fresh-Chromium-profile browser proof: link
  click swaps to Details with "Viewing: Details", Back home restores exact home pixels, 0 page errors/overlays.
  Full suite 575+17+30 pass, node studio 14 pass. Gallery still conversion-failed (next: WindowChrome/automation
  boundaries). Retained limits: no journal/NavigationService, no Uri navigation, no
  Navigating/LoadCompleted/NavigationFailed, no NavigationWindow.Navigated, no WebResponse.
- File dialogs + Clipboard (2026-10-23): gallery FileAndFolderDialogsPage/ClipboardPage slice. New `FileDialogs.cs`
  (`Microsoft.Win32` namespace): `CommonDialog` (Title, FileOk, instance/static presenter, ShowDialog true/false/null,
  FileOk veto, no-presenter precise error, presenter OperationCanceledException maps to cancel), abstract
  `FileDialog` (FileName(s)/SafeFileName(s), Filter parsing + MatchesFilter, FilterIndex, DefaultExt/AddExtension,
  InitialDirectory, CheckFileExists/CheckPathExists/ValidateNames/DereferenceLinks/RestoreDirectory, OpenFile stream),
  abstract `CommonItemDialog`, `OpenFileDialog`, `SaveFileDialog` (CheckFileExists false default, Overwrite/CreatePrompt,
  writable commit-on-dispose OpenFile), `OpenFolderDialog` (FolderName(s), bare mount root valid). New
  `WasmVirtualFileSystem` (Core): session-scoped declared mounts, clone-on-read, commit-on-dispose writes, absolute/
  traversal/undeclared paths are loud errors, instances isolate sessions. New `Clipboard.cs`:
  `TextDataFormat` (plain Text/UnicodeText only; rich formats throw), text/image/file-drop store, ContainsAudio false,
  sync session semantics plus explicit FlushAsync/RefreshAsync device mediation (denial -> ClipboardException,
  cancellation -> OperationCanceledException). `WpfTypeMap` Overrides map the six Win32 dialog types; Clipboard maps
  by default rule. 22 FileDialogClipboardTests (round-trip, cancel/null/dismiss, veto, filters, AddExtension, names,
  unstaged/missing, save commit, folder roots, isolation, reset, owner overload) + Convert MapCoversFileDialogClipboardSlice
  + demo wiring test green; gallery re-packaged 18 -> 11 gaps (28 staged, input hashes verified); new engine-native
  files-clipboard demo (DeviceBridge/Noter browser seams, layout notes) proves simulated pick, cancel, real
  file-chooser staging, real download, real navigator.clipboard round-trip and denial repaint with 0 page errors;
  baselines/latest.json covers 27 demos. Retained limits: no IDataObject, no native picker chrome/owner modality,
  no single static cross-session presenter, no process CWD, no audio payloads. Gallery still conversion-failed
  (next: navigation events).
- Validation + Hyperlink (2026-10-23): gallery TextBoxPage validation and
  HyperlinkPage/ClipboardPage navigation slice. New `Validation.cs`:
  `ValidationRule` (abstract, ValidationStep Raw/Converted/Updated/Committed,
  ValidatesOnTargetUpdated), `ValidationResult` (+ValidResult), `ValidationError`,
  `ValidationErrorEventAction/EventArgs` and the `Validation` attached-error
  surface (GetHasError/GetErrors live list, Add/RemoveErrorHandler over the
  `ErrorEvent` routed token). `Binding` gains `ValidationRules` +
  `NotifyOnValidationError`; `BindingExpression` push order is raw (blocks
  source update) -> converted (blocks) -> Updated/Committed (report only), and
  target updates run ValidatesOnTargetUpdated rules; only NotifyOnValidationError
  bindings raise Error (Added/Removed, bubbling). TextBox reordered to push
  before TextChanged (WPF DP order) and paints `ErrorBorderRgba` while invalid.
  New `Hyperlink : Span` (NavigateUri/TargetName, Click + RequestNavigate CLR
  and routed events, DoClick press/release + Enter/Space, focus ring);
  TextBlock hosts inlines in logical children with folded text, per-link rects,
  blue+underline paint and hit routing; loader parses `<Hyperlink>` (+wires) and
  `<Binding.ValidationRules>`. Session navigation stays in-session: unhandled
  absolute URIs are a precise boundary (no external launch); hosts handle
  RequestNavigate (demo swaps its Frame). 14 ValidationTests + 14
  HyperlinkTests + demo wiring test + Convert MapCoversValidationHyperlinkSlice
  green; gallery re-packaged 23 -> 18 gaps (28 staged assemblies); new
  engine-native validation-links demo proves invalid typing (425 red px), link
  navigation and back-home in the browser with 0 page errors; baselines/
  latest.json covers 26 demos. Retained limits: no Validation.Error XAML
  attached syntax, no BAML Hyperlink/Span ids, single-line links, no external
  launch, no journal/NavigationService. Gallery still conversion-failed (next:
  dialogs + Clipboard slice).

## Historical implementation checkpoints

- SizeChanged events + Ellipse shape (2026-10-23): gallery TileGallery/UserDashboard avatar slice. New `SizeChangedEventArgs` (PreviousSize/NewSize, WidthChanged/HeightChanged) + `SizeChangedEventHandler`; `UIElement.Arrange` detects real size deltas and `FrameworkElement` raises `SizeChanged` (same-size re-arranges silent), with `RenderSize`/`ActualWidth`/`ActualHeight` reflecting the last pass. New `Shapes.Ellipse` (Fill/Stroke/StrokeThickness, true-oval hit test) paints through the retained `DrawEllipse` op (JSON op 6, Skia oval, Canvas2D ellipse). XAML loader parses `<Ellipse>` attributes and `SizeChanged` codebehind wires. 7 SizeChangedTests + 7 EllipseTests (arrange deltas, flags, silence, RenderSize, wire recording, fill/stroke Skia pixels, op-6 JSON, hit shape, demo resize) + Convert MapCoversSizeChangedEllipseSlice green. Gallery re-packaged with the current engine: 26 -> 23 conversion gaps (28 staged assemblies). New engine-native size-ellipse demo: browser proof shows avatar ink (B/R/Y ~1220px each), Space-activated Grow tiles repaints (150px changed bbox), ink persists, 0 page errors; baselines/latest.json covers 25 demos. Retained limits: no LayoutUpdated event, no ActualWidth data binding without INotifyPropertyChanged, stroke-only ellipse has transparent fill op. Gallery still conversion-failed (next: ValidationRule/Result + Hyperlink/RequestNavigate slices).
- Range/Slider controls (2026-10-23): gallery BasicInput/SliderPage + UserDashboard age-slider slice. New `Slider.cs`: abstract RangeBase (Minimum/Maximum/Value coercion, SmallChange/LargeChange, ValueChanged with coerced old/new values, snap-to-tick) and Slider (horizontal/vertical track + thumb, tick marks, pointer press/drag with grab offset, track-press LargeChange step, keyboard control, focus ring, TwoWay binding push). Engine `RoutedPropertyChangedEventArgs<T>`/handler and TickPlacement (None default, verified against WPF docs) close the converter map; XAML loader parses `<Slider>` attributes and ValueChanged wires. 11 SliderTests (coercion, events, snap, drag, keyboard, binding, paint, focus-ring outline regression) + Convert MapCoversSliderSlice green. Gallery re-packaged with the current engine: 31 -> 26 conversion gaps (28 staged assemblies). New engine-native slider-lab demo: browser proof drags 25 -> 90 with live readout, End/Home keys repaint, 0 page errors; baselines/latest.json covers 24 demos. Retained limits: no Ticks collection, no track-press auto-repeat, no selection range, no IsMoveToPointEnabled. Gallery still conversion-failed (SizeChanged + Ellipse closed next in the entry above; then validation/hyperlink slices).
- App-level resources + 2D media (2026-10-23): original Geometry boots through
  Application.Resources hydration (DrawingBrush grid brushes, gradients, geometry
  entries with lexical `{StaticResource}` refs) plus a real media/template stack:
  path mini-language, shape geometries, CombinedGeometry union paint,
  Drawing/DrawingGroup/GeometryDrawing, TileBrush/DrawingBrush/VisualBrush,
  gradients, Path/Rectangle shapes on DrawGeometry ops, Viewbox, ContentPresenter,
  Span/Run/LineBreak, NavigationWindow page host, ControlTemplate retention with
  deferred TemplateBindings, and live EventTrigger storyboards. RadioButton
  template + radio-select storyboard chain proven; Geometry re-packaged with 0
  gaps, `Convert boot`/`press` clean. Browser baseline `20261023T083043Z`: 23
  demos, 0 page errors, all 15 launcher checks; geometry 57586 B, 852596 ink px
  (WebGL 201065 ink). Full suite 487+12+30 pass; Media3DTests 11/11 (VisualBrush
  material now loads; ImageBrush stays the precise 3D-material boundary).
  Retained limits: template inflation, tiled-brush fallback paint, union-only
  combine, single-line text metrics, unlit VisualBrush. Compat report verified
  current (geometry Built/converted/1/0).
- Original application pipeline (2026-10-23): all 14 pinned upstream projects
  build through full-source preparation. Dependency-aware packaging replaces
  the hard-coded two-app runtime loader; only referenced local assemblies are
  converted and staged as hashed images, with per-assembly gap reports and
  hash-checked dynamic loading. Launcher clicks attempt real packages instead
  of stopping at preparation labels. Startup automatically prepares stale or
  missing packages; engine/source/image fingerprints govern reuse. Original
  SimpleBinding now works through real resource objects, styles, labels,
  TwoWay binding and assembly-scoped resources. Binding/dialog ABI constants
  match WPF, and rendered bounds honor explicit size/alignment independently
  of allocated layout slots. Observed checkpoint: 359 full .NET tests, 14 Node
  tests, 22 CPU/GPU browser apps, 15 launcher checks and all 14 package reports
  pass. Three original apps run; eleven larger apps still have actual missing
  WPF APIs. See `docs/application-conversion-validation-2026-10-23.md` for the
  frozen browser engine identity, concurrent-source boundary and evidence,
  and `docs/application-compatibility-status.md` for ordered remaining work.

- Integrated renderer (2026-10-23): removed the standalone GPU spike. Actual
  application commands from WasmWpfHost.CurrentCommands now replay through
  the shared SkiaCommandRenderer onto SKGLView/WebGL2, with Canvas2D fallback.
  Workspace controls switch renderer without rebuilding the app; context loss
  and GPU retry retain state. Native raster replay shares gradients, paths,
  clips/transforms, bitmap resources and bounded paint/font caches. Shared
  Noto fonts, real GPU screenshots, optional CPU-submission diagnostics and
  a regression for off-diagonal affine terms are included. Reconciled the
  concurrent converted CalculatorDemo: retained app-visible Name, WPF-shaped
  Parent, full signature rewriting and enum identity, plus global-state test
  isolation. Final observed full suite: 344 .NET and 12 Node tests pass;
  fresh Chromium tour: 21 CPU and 21 actual GPU apps, switching/recovery and
  15 launcher checks pass with no normal browser errors. Tests use a static
  Debug snapshot to avoid concurrent live asset-map races. See
  `docs/integrated-renderer-2026-10-23.md` for evidence and remaining limits.

- Preview upgrade (2026-10-23): independent HTML/JS WPF Studio launcher with
  search/readiness/category filters, app/source details, top-level measured
  load/bytes/HTTP reuse, per-app iframe sessions, strict message ownership,
  error reports, stop/restart and live canvas screenshot downloads. Original
  16 scenes preserved; four working engine-native mini-apps added; 13 external
  conversion targets exposed and eight pinned source subsets prepared locally.
  HelloWorld is downloaded on demand through physical static-asset staging,
  not embedded in the shell. Frame calls no longer overlap or update the menu
  every frame; resize invalidates the scene. Verification: 20-scene headless
  tour, 15 launcher checks and 11 Node contracts pass. Full .NET suite still
  has three engine/converter failures; see `docs/preview-validation-2026-10-23.md`.
  No engine/converter implementation was changed in this preview slice.

- Step 2 (done, minimal): `DependencyObject` / `DependencyProperty` /
  `Freezable` / `WasmDispatcher` over `IWasmMessagePump`, zero P/Invokes.
- Step 3 (done, minimal): `Measure` / `Arrange` for `Border`, `Grid`,
  `StackPanel`, `Canvas`, `Button`, `TextBlock`, `Window`.
- Step 4 (done, minimal): `DrawingContext` records replayed by `WasmMilCore`
  into `ISharedGraphicsSurface`; `ExportFrameJson` feeds `wfwasm.paint`.
- Step 5 (done): `WasmXamlReader` text-XAML binding (elements, attributes,
  property-element content, Grid.Row/Column + Canvas.Left/Top attached props,
  x:Name namescopes, line-info errors); real hit-testing + tunneling/bubbling
  routes + focus in `WpfInputManager`; `Button` raises `Click` on the hit
  target. (Binary MS BAML was rejected here; see Step 15 — it now loads.)
- Step 6 (done): HarfBuzz shaping via SkiaSharp.HarfBuzz (`TextShaper` in
  Graphics: shaped measure, embedded + system + default typeface resolution,
  Skia fallback reported); `DrawText` replayed in `SkiaSurface` with shaped
  alignment; `GlyphRun`/`FormattedText` (+ellipsis) facade; `WasmWpfHost.Tick`
  renders windows and exports the `wfwasm.paint` transport frame
  (`WpfTransportFrame`: JSON ops + out-of-band uploads).
- Step 7 (done): `IFrameSink` + `FramePublisher` in Graphics (uploads first,
  then one paint call; cancelled publish paints nothing); `WasmWpfHost.PublishAsync`
  drives the shared canvas host path; `TextBlock`/`Button` measure with shaped
  `TextShaper` metrics, legacy minimums preserved, `FontCss` per element, never throws.
- Step 8 (done): `WindowsBase` full-copy staging in `DesktopWasm.BrowserHost`
  following the WinForms `PrepareFullWindowsBase` pattern (`Prepare` stages via
  `scripts/Ensure-FullAssembly.ps1`, `Use` swaps the facade, `Gate` runs
  `scripts/BundleCheck.ps1`). Verified live: facade swapped for the 2.2MB full
  copy, gate PASS (integrity + DependencyObject). Fail-soft discovery (engine is
  facade-free; staging is merge preparation), hard error on explicit missing source.
- Step 9 (done): host page wiring — keyboard/mouse/wheel DOM events into
  `WpfInputManager`, `requestAnimationFrame` loop driving `Tick`/`PublishAsync`
  via `wasmWpfLoop`, demo menu (`/`, `/demo/{slug}`) mirroring the WinForms
  library with six engine-native scenes; `WindowsFormsHost`/`ElementHost`
  zero-copy interop path (`RenderHeadless`); `ParseColor` public for
  code-built scenes.
- Step 10 (done): P/Invoke neutralization proof — committed tests assert zero
  `PinvokeImpl` methods across Core/Graphics/Wpf plus staged-copy size and
  `DependencyObject` presence.
- Step 11 (done): dirty-region tracking for the rAF loop — `Tick` returns the
  cached frame when clean, `PublishAsync` skips sink interop and replays the
  cached result; any routed input or explicit `Invalidate()` re-arms rendering.
- Step 12 (done): host restyle to match the WinForms App Studio — shared
  `css/app.css` theme, sidebar demo library (search, tabs, grouped entries),
  toolbar with live dot, stage with loading/error/unknown overlays, footer
  frame status, icon rail; auto-start rAF loop.
- Step 13 (done): browser hardening, verified in headless Chromium — SkiaSharp
  ships no WebAssembly natives, so `TextShaper` short-circuits on
  `OperatingSystem.IsBrowser()` (never touches a Skia type; JSON still carries
  text for Canvas2D), `ReplayText` and the surface-less `WasmWpfHost` keep the
  browser Skia-free; fixed mixed `0xAARRGGBB`/`0xRRGGBBAA` element defaults to
  the shared `0xRRGGBBAA` convention (plus a pinning test). Loop runs clean
  past 970 frames with zero console/page errors, button + text paint verified
  pixel-by-pixel.
- Next: `WindowsBase` consumers that need real IL beyond the facade swap.
- Step 17 (done): SimpleBinding v1 — `DataContext` with parent-chain
  inheritance (refresh cascades the subtree; late `AddChild` re-resolves),
  `{Binding [Path][, Mode=OneWay/OneTime]}` in XAML on plain CLR properties,
  `FrameworkElement.SetBinding` returning a live `BindingExpression`,
  INotifyPropertyChanged push (first-segment match; OneTime never subscribes),
  explicit `Source`, dotted paths, invariant value conversion, missing
  source/path leaves the target untouched. TwoWay/ElementName/Source=/attached
  bindings are precise errors, not silent gaps. New `binding` demo (Data group):
  XAML text bound to an INPC view-model, Birthday button pushes Age 36→37
  live. 21 new tests; full suite 118+23 green; 8/8 demos render with zero
  console/page errors; live Birthday click repaint verified headless.
- Step 16 (done): GPU spike — pinned Skia browser route (review finding 7).
  Bumped SkiaSharp/SkiaSharp.HarfBuzz 4.148.0 → 4.153.1 (whole-graph lockstep,
  one libSkiaSharp per process), added SkiaSharp.NativeAssets.WebAssembly +
  SkiaSharp.Views.Blazor 4.153.1 to BrowserHost only. New `/gpu-spike` page
  (linked under Diagnostics): SKGLView with EnableRenderLoop painting text,
  nested clips, transforms, alpha and a decoded-PNG upload; SKCanvasView
  fallback; live WebGL2 probe, surface/DPR, cold/warm/input-latency/CPU
  metrics, context-loss simulation with real failover, restore polling,
  manual backend toggle. Measured headless (SwiftShader): WebGL2
  ES 3.0 Chromium/ANGLE, 640×360 samples-1 stencil-0, cold ~750ms
  (incl. 500ms deferred mount), warm paint ~0.7ms, input→paint ~8–14ms,
  engine CPU command build ~60–90ms, GL scene 185 distinct colors vs 181
  CPU. Context loss → fallback live; headless SwiftShader refuses restore
  (environment limit — restore path polls `isContextLost` + honors the
  `webglrestored` event; remount verified via toggle round-trip). Deferred
  mount eliminated the Views.Blazor init race ("No canvas element" +
  SizeWatcher noise: 0). Removed the `IsBrowser` shaping carve-out —
  HarfBuzz shaping now runs in browser-wasm (demos re-verified); corrected
  the stale "ships no WASM natives" comments in TextShaper/WasmWpfHost/Home
  and suppressed the inert-payload WASM0001 scan noise for the staged
  WindowsBase copy (Step-10 neutralization tests stay the real guard).
  KEEP (not revert): natives + spike page stay; Canvas2D remains the demo
  transport by design, GL is proven and available per-scene.
- Step 14 (done): native replay correctness in `SkiaSurface` — `PushTransform`
  translation now lands in `transX/transY` (was: perspective row, collapsing
  translated paint); every replayed `SKPaint` deterministically disposed via
  `using` (+ `FillPaint` factory); pixel regression tests
  (`TransformReplayTests`: translation, scale guard, pop-restore).
- Step 15 (done): real compiled-MS-BAML loading (`BamlReader` v1, no
  WindowsDesktop dependency) — header/record framing validated against the
  pinned 1222-byte HelloWorld `MainWindow.baml` (Window/Grid/TextBlock,
  Title/Icon/Height/Width/HAlign/VAlign, String/Length/Enum/ImageSource
  converters; root custom x:Class type maps to plain `Window`, codebehind
  ignored); every other record/type/member fails with a record-offset error.
  `XamlLoader.LoadComponent` sniffs the `MSBAML` signature and routes BAML
  bytes through the reader (loose XAML unchanged). Verified end-to-end by an
  out-of-repo probe: real BAML → Window "MainWindow" 525x350, Grid, centered
  "Hello, World!" `TextBlock`, headless render emits the greeting at
  (348, 290). Parity fixes found by the probe: loose XAML now also accepts
  `VerticalAlignment` on any `FrameworkElement` and retains `Window`
  Width/Height/Icon (were: rejected/ignored while BAML preserved them).
- Step 18 (done): TextBox editing. `TextBox` control (single-line editor:
  Text/FontSize/Foreground, border chrome, caret, focus-gated keyboard
  editing — type, Backspace/Delete, arrows, Home/End; Enter ignored,
  Ctrl-combos ignored), per-element `OnGotFocus`/`OnLostFocus` hooks fired
  through a centralized `SetFocused` gate, and completed TwoWay binding
  (`UpdateSource` with skip-if-equal cycle guard; XAML `Mode=TwoWay` now
  parses). BAML TextBox support (-639, Text member -114, ConnectionId 0x2D
  fixed 5-byte framing, Name -54 as namescope-only) verified against a real
  876-byte TextBox fixture built with net10.0-windows (two ConnectionIds 1,2;
  member ids cross-checked against the desktop node stream), plus XAML/BAML
  parity and headless render through the same probe (23/23 checks). 17 new
  tests; full suite 136+23 green. Binding demo gained a TwoWay-bound TextBox;
  8/8 demos render with zero console/page errors and live Tab+typing repaints
  headless (TwoWay push visibly updates the bound TextBlock). Lesson logged:
  always `dotnet build` the slnx before serving — test runs skip the host
  project, so `--no-build` serves stale bundles.
- Step 30 (done): First converted real WPF app in the gallery (shim, not port).
  `src/DesktopWasm.Convert` (Cecil rewriter + `scan`/`convert`/`boot` CLI)
  mirrors the WinForms pipeline: name-based framework→engine remap
  (`wpf-convert-v1`), loose-XAML embedding, framework-ref binding to the
  host runtime generation, dead designer-ballast stripping (live uses over
  missing libraries are gaps, never silent strips), per-binary receipt with
  source pin + input hash. Engine gained `Application.LoadComponent(object,
  Uri)` hydration, `Uri StartupUri`, `IComponentConnector`, throwing `Run()`,
  `/Assembly;component/` pack shorthand, and `WpfAppBoot.Boot` (create
  Application, run generated InitializeComponent, resolve StartupUri, return
  MainWindow). First target: WPF-Samples HelloWorld @ `811d01e` (targets/
  read-only, native baseline + scan + generated-code review in E:/tmp):
  zero-gap conversion, desktop `boot` oracle green, gallery "Real apps" entry
  (`/demo/hello-real`, embedded converted DLL + receipt with map check)
  rendering "Hello, World!" headless with hello-real→controls navigation
  proven and no leaked browser processes. 5 boot + 1 pack-uri engine tests and
  7 converter tests; full suite 271+7+23 green.
- Step 31 (done): Second converted real app in the gallery (CalculatorDemo,
  shim, not port) + converter signature fidelity. The calc's `MyTextBox`
  override exposed a structural-rewrite gap: member references and override
  rows were rebound by declaring type only, leaving real-WPF parameter/return
  types behind (`MissingMethod` on first focus/click). `RewriteSignature` now
  rewrites every method-reference identity (declaring type + signature, incl.
  generic instantiations and attribute constructors; idempotent so shared
  local definitions are never corrupted) and the live-graph validator collects
  the same signature positions, so orphans fail loudly again. Engine gained the
  real-WPF-shaped `FrameworkElement.Parent : DependencyObject` shim (same
  stored link as `UIElement.Parent`) that converted `Button.Parent` walks bind.
  Gallery: staged `ConvertedApps/CalculatorDemo` binary + receipt, generalized
  on-demand loader (`s_onDemandApps` in Home.razor) serving
  `/demo/wpf-calculator`, `BootConverted` names the app in map-mismatch errors.
  Proof: `ConvertedCalcInteractionTests` (routed Down/Up at arranged centers:
  `1 2 + 3 =` shows `15`, File menu + `5 C` clear path) and headless browser
  run (fresh profile, engine-measured centers, canvas repaints, controls
  navigation, zero console errors, no leaked processes). Full suite 306+8+30
  green plus 13 node staging tests.
- Step 32 (done): Third converted real app in the gallery (SimpleBinding,
  shim, not port) + TextBox Backspace caret fix. Native baseline built in
  E:/tmp/wpf-simple from the read-only targets checkout: `scan` clean (no
  P/Invokes), zero-gap `convert` (dead Settings type + ConfigurationManager
  pruned, App/MainWindow XAML embedded), desktop `boot` oracle green (Border
  over DockPanel, 525x350). Staged via the manifest-driven package path
  (`wwwroot/apps/simple-binding`: hash-named binary + `app-manifest.json`,
  status `converted`); the parallel launcher work had already flipped the
  gallery entry to `ready` with converted boundary text, so this step validated
  it end to end (tests, browser, baseline). Real engine bug found by the new
  test: Backspace at end of
  text double-stepped the caret (the Text setter already clamps into the
  shortened text, then the handler decremented again), leaving "o" after
  clearing "Joe"; the handler now decrements first and removes at the new
  caret. Proof: `ConvertedBindingInteractionTests` (resource model + labels
  boot state; click-focus, End, clear, type "Ann" pushes through TwoWay
  `UpdateSourceTrigger=PropertyChanged` into the Person model and the bound
  TextBlock) and headless browser run (fresh profile, editor click + typing
  repaints canvas, controls navigation paints, zero console errors, profile
  deleted, server + orphan devserver stopped, port 5199 free). Baseline
  regenerated via `bench.py --serve --update-baseline` (22 slugs incl.
  simple-binding at dataLen 35918 matching the probe; 15/15 launcher checks,
  zero errors). Full suite 317+10+30 green. Geometry stays out of reach
  (16 gaps: Page/RadioButton/ToggleButton/Frame/Visibility/Storyboard plus a
  Media3D/Viewport3D subsystem); wpf-gallery/screen-to-gif fail at native
  build; heavier suites need control-template/visual-state coverage.
- Step 33 (done): Toggle controls, Page/Frame navigation and Visibility
  (Geometry gap shave 16 -> 11, shim, not port). Engine gained `Visibility`
  (Hidden/Collapsed skip paint via a `RenderVisible` choke point at all 11
  container render sites + roots/overlays, and skip hit-test at the
  `HitTest` base plus menu overrides; Collapsed keeps its layout slot as a
  documented v1 limit), `ToggleButton` (`bool? IsChecked`, Checked/
  Unchecked/Indeterminate, press toggles then Clicks), `CheckBox`
  (two/three-state cycle), `RadioButton` (GroupName + same-parent implicit
  group, exclusivity on check, checked press still Clicks), `Page`
  (WindowTitle + content host, renders without window chrome) and `Frame`
  (content host, `Navigate(object)` + `ContentRendered`, Uri/non-element
  Navigate stays a precise error), plus `FrameworkElement.Loaded` with a
  `RaiseLoadedTree` parents-first walk. `WpfAppBoot.Hydrate` learned the
  Page branch (title/resources/content/fields/events, then Loaded) and
  `WireEvents` now retargets parsed-root wires onto the component (root
  `Loaded=` handlers were silently dropped) and binds `(object, EventArgs)`
  handlers through a forwarder (Geometry's FrameContentRendered shape).
  XAML loader cases for all six types plus attributes (IsChecked/GroupName/
  IsThreeState/Visibility/WindowTitle/NavigationUIVisibility), property-element
  and inner-text content, child attach, and precise errors for bad enum
  values. 10 new tests (`ToggleNavigationTests` + `NavPage.xaml` fixture incl.
  a nested-class `x:Class` and an EventArgs handler). Re-packaging Geometry
  against the release binary (output to E:/tmp, sibling manifests untouched)
  confirms 11 remaining gaps: 7 Media3D, Viewport3D, Storyboard, Timeline,
  ConfigurationManager. Full suite 329+10+30 green plus 14 node tests.
- Step 39 (done): Mouse + traversal (compat item 4, third slice;
  gallery 79 -> 75, gif 497 -> 479). New `Input.cs` enums
  (`MouseButton` 0-4, `MouseButtonState` 0-1, `FocusNavigationDirection`
  0-7, `TraversalRequest`, all verified against Learn) and
  `InputDevices.cs` mouse layer: `MouseEventArgs` (+Button/Wheel) with
  live button states, `MouseDevice` (captured/directly-over/override
  cursor), `Mouse` statics (capture, position, buttons), `Cursor`/
  `Cursors` (19 shared instances). UIElement gains Preview/Mouse
  down/up/move/wheel + enter/leave events, `IsMouseDirectlyOver`/
  `IsMouseOver` (interface now complete) and `MoveFocus(TraversalRequest)`
  (tab-order moves work; directional returns false: no spatial nav).
  The manager tracks pointer position/held buttons, per-element hover
  routes with enter/leave diffing, double-click counting (500ms/4px),
  and explicit capture that wins over hit testing; handled previews
  suppress the Wasm route and clicks. 14 new probes; 8 new
  `MouseInputTests`. Deferred: commands, stylus (needs pointer-type
  plumbing). Full suite 390+10+30 green plus node 14/14.
- Step 38 (done): Input devices + dispatcher ops (compat item 4, second
  slice; gallery 83 -> 79, gif 530 -> 497). New `InputDevices.cs`:
  `InputEventArgs`/`KeyboardEventArgs`/`KeyEventArgs` (internal ctors,
  tolerant `KeyFromDomName`), `KeyEventHandler`, `KeyboardDevice` +
  `Keyboard` statics (focus/modifiers/held keys tracked per manager),
  `IInputElement` (keyboard-subset interface, implemented by UIElement
  with CLR focus events raised from the single focus gate),
  `PresentationSource` (abstract, `FromVisual` returns null: no composed
  target) and `TextComposition` (also exposed on
  `TextCompositionEventArgs`). New `Input.cs` surface: `InputGesture`,
  `KeyGesture` (matches/display strings), `KeyGestureConverter`/
  `KeyConverter`/`ModifierKeysConverter` (string parsing over the BCL
  TypeConverter), `KeyInterop` (US-layout best-effort both directions,
  0/None fallback). `Threading.cs`: `DispatcherOperation(+Status,
  Verified Pending=0/Aborted=1/Completed=2/Executing=3)` with Wait/Abort/
  Task/Result, `DispatcherTimer` (host-ticked wall time + test clock),
  `DispatcherUnhandledExceptionEventArgs/Handler` wired into Invoke and
  operation faults, `ComponentDispatcher` modal/filter hooks.
  `WpfInputManager` raises Preview/Key CLR events around the Wasm route
  (handled preview suppresses everything; handled keys skip Tab/Enter
  defaults) and tracks held keys/modifiers. 22 new `WpfTypeMap` probes;
  12 new `InputDevicesTests`. Fixed a full-suite-only thread-affinity
  flake (statics bind the pump to first-touch thread; tests rebind
  `WasmDispatcher.Current` per class). `BeginInvoke` now returns
  `DispatcherOperation` (real shape; Step 37 tests updated). Deferred:
  Mouse/Cursors/mouse events, commands, TraversalRequest, stylus.
  Full suite 382+10+30 green plus node 14/14.
- Step 37 (done): Threading + key enums (compat item 4, first slice;
  gallery 86 -> 83, gif 551 -> 530). New `Threading.cs`:
  `DispatcherPriority` (Invalid -1 through Send 10),
  `DispatcherObject` (Dispatcher/CheckAccess/VerifyAccess) and `Dispatcher`
  (CurrentDispatcher singleton over the shared single-threaded
  WasmDispatcher; Invoke runs inline, BeginInvoke defers through the pump,
  priorities accepted but unordered, no shutdown, Task-based InvokeAsync).
  New `Input.cs`: full `Key` table (202 members incl. Return=Enter and
  Capital=CapsLock aliases) and `[Flags] ModifierKeys`. All tables
  generated from the Microsoft Learn API reference (script in /tmp, see
  entry) instead of hand transcription — which caught two wrong
  assumptions (WPF F1=90 not 112, D0=34 not 48: WPF Key values are not
  Win32 VK codes). Fixed a stack overflow the first test run exposed
  (CheckAccess recursed through the Dispatcher property; now routes
  straight to the pump). `WpfTypeMap` probes cover 5 new names. 5 new
  `ThreadingTests` pin spot values, aliases, counts, flags and dispatcher
  inline/defer behavior. Remaining in this family (next slice):
  DispatcherTimer/DispatcherOperation(+Status), KeyEventArgs/Handler,
  Keyboard/Device, KeyGesture(+Converter), KeyInterop, ComponentDispatcher.
  Full suite 370+10+30 green plus node 14/14.
- Step 36 (done): Foundational ABI, values/brushes/DP callbacks (compat
  item 1, first slice; gallery 94 -> 86, gif 591 -> 551). New
  `Foundation.cs`: `Point`/`Vector` (mutable, operators, Parse, invariant
  formatting), `Size` (non-negative validation, `Empty`), `Rect` (corners,
  Contains/Inflate/Intersect/Union/Offset, `Empty`), `Int32Rect` (HasArea,
  Parse), each with `==`/`!=`/`Equals`/`GetHashCode`/`IFormattable` and
  implicit bridges to the engine `WpfPoint`/`WpfSize`/`WpfRect` records.
  New `Brushes.cs`: abstract `Brush` (`Opacity`), sealed `SolidColorBrush`
  (parameterless = no color, `Color`, frozen-setter guard, engine `ToRgba`),
  `Colors`/`Brushes` (30 named entries incl. every color/brush ScreenToGif
  and WPFGallery reference; `Brushes` returns frozen shared instances).
  `DependencyObject.cs`: `PropertyChangedCallback`,
  `DependencyPropertyChangedEventArgs`, callback overloads on
  `PropertyMetadata`/`FrameworkPropertyMetadata`, `DependencyPropertyKey` +
  `RegisterReadOnly` (enforced), `UnsetValue`, `RegisterAttached`,
  `AddOwner`, `OverrideMetadata`/`DefaultMetadata` (most-derived wins at
  lookup), `ClearValue`/`SetCurrentValue`/`InvalidateProperty`
  (documented no-op), standard `OnPropertyChanged(args)` virtual,
  `Freezable.CanFreeze`/`Clone` (detached store)/`GetAsFrozen`.
  `WpfTypeMap` probes cover 12 new names. 13 new `FoundationAbiTests`;
  member shapes were driven by a Cecil scan of the real WPFGallery/
  ScreenToGif binaries and cross-checked against Microsoft Learn
  (`Int32Rect`, `SolidColorBrush()`). Deferred openly: Geometry/Pen/
  DrawingBrush/gradients, Control/Visual/ItemsControl members, Dispatcher,
  `Key`/`ModifierKeys`/other enums, Clipboard/SystemParameters/SystemColors.
  Full suite 365+10+30 green plus node 14/14.
- Step 35 (done): Media3D wireframe Viewport3D (shim, not port; Geometry
  gap shave 9 -> 0, package status converted). New `Media3D.cs`: `Color`/
  `Point3D`/`Vector3D` (XAML parse), `Point3DCollection`/`Int32Collection`/
  `PointCollection`, `Transform3D` hierarchy (translate/scale/rotate-about-
  center/group with Rodrigues axis-angle math), `GeometryModel3D`/
  `MeshGeometry3D`, `DiffuseMaterial`, `AmbientLight`/`DirectionalLight`
  (parsed and stored; the wireframe renderer is unlit), `PerspectiveCamera`
  and `Viewport3D` (FrameworkElement rendering projected triangle edges as
  `DrawLine`, fully transport-supported; no rasterizer, lighting, textures,
  culling or hit-testing). XAML loader reads the 3D surface
  (`Viewport3D.Camera`/children property elements, `ModelVisual3D`,
  cameras/lights/materials/transforms/rotations with x:Name into the
  generalized object namescope; VisualBrush/ImageBrush, orthographic
  cameras and non-visual nesting stay precise boundaries). Timeline XAML
  surface completed: `Completed=` on resource storyboards/animations wires
  through `EventWires` (standalone dictionaries reject it: no code-behind),
  plus `FillBehavior`/`AccelerationRatio`/`DecelerationRatio` attributes;
  TargetName resolves through the `NameScopes` registry fallback so
  rotations/scales animate (covers the real SampleViewer `ZoomInStoryboard`
  shape: Completed + FillBehavior=Stop + ratios 1). `WpfTypeMap` probes
  cover 29 new engine names and `CreateElement` builds `Viewport3D`.
  Geometry re-packaged to E:/tmp: 9 -> 0 gaps, converted, staged under
  `wwwroot/apps/geometry` with the gallery entry boundary updated
  (not-converted: boot stops at `<Application.Resources>`, the Step 36
  wall); gallery/gif re-counted unchanged (94/591, neither references 3D).
  12 new tests (`Media3DTests`: parse, rotation math, projection,
  wireframe render, real-sample XAML scene, direct + namescope rotation
  animation, Completed/FillBehavior/ratios, precise failures;
  `ConvertedGeometryInteractionTests`: staged manifest validation incl.
  sha check, fail-loud boot boundary). Full suite 352+10+30 green;
  headless verify ALL PASS (home, geometry fail-loud naming the boundary,
  simple-binding paints 36k, zero page/console errors, fresh profile
  deleted, server + orphan devserver stopped, 5199 free). Baselines
  untouched (geometry is not gallery-ready, so no new baseline is due).
- Step 34 (done): Deterministic animation clock (shim, not port; Geometry
  gap shave 11 -> 9). New `Animation.cs`: `Duration` (Automatic/Forever/
  TimeSpan, XAML parse), `RepeatBehavior` (count/Forever/duration, XAML
  parse), `Timeline` (Duration/BeginTime/RepeatBehavior/AutoReverse),
  `TimelineCollection`, `DoubleAnimation` (From/To/By, IEasingFunction hook,
  linear until easing classes land), `Storyboard` (children, Completed,
  Set/GetTarget(Name/Property) via weak-key tables, Begin(scope)/Stop) and
  an internal `WpfAnimationClock` that only moves through `Advance` (tests)
  or `PumpRealTime` (one clamped frame delta per `WasmWpfHost.Tick`, so
  animated setters repaint through the usual Invalidated path with no host
  changes). HoldEnd settles the exact final edge (reversed edge for
  AutoReverse, frozen in-flight value for duration caps); unsupported
  children, dotted paths and non-double targets are precise errors.
  XAML loader reads Storyboard/DoubleAnimation resource entries (nested
  children, Storyboard.TargetName/TargetProperty, precise errors) and the
  map probes cover all seven types. `FrameworkElement.FindResource` added
  for converted lookup callers. 11 new tests (`AnimationTests`: midpoint/
  end, From-current/By, BeginTime+count, AutoReverse+Forever, Stop/restart,
  TargetName scope, precise failures, XAML parse + garbage rejection,
  Duration/RepeatBehavior round-trip). Re-packaging to E:/tmp (sibling
  manifests untouched): geometry 11 -> 9, gallery 100 -> 94, gif 606 ->
  591; all 7 new types absent from the fresh gap lists (as are the 6 from
  Step 33). Remaining wall
  for animated apps: Media3D/Viewport3D rendering, key-frame/easing
  classes, Style/Trigger/ControlTemplate loading. Full suite 340+10+30
  green plus 14 node tests; browser smoke (controls/binding/menu paint,
  zero console errors, profile deleted, server + orphan stopped, 5199 free).
- Step 29 (done): Evidence bundle for the harness (review plan item 9, first slice).
  `bench.py` now records app identity (engine assembly SHA256 pin; refuses
  unbuilt trees), harness pin, environment (viewport/DPR/user agent/fonts/
  transport/cold cache), per-demo pixel hashes + performance-API network
  timings, `/gpu-spike` self-reported backend metrics, a coordinate-level
  action log with before/after hashes, and the error log; only the gated
  subset (blanks, FPS < 70%, interactions, errors) fails a run. README
  documents evidence-vs-gates; `BaselineTests` pins environment/app fields.
  0 engine changes; suite stays 265+23 green.
- Step 28 (done): Project basics (review plan item 10, first slice).
  `README.md` (architecture, shim philosophy, browser differences, build/test/
  serve/bench, layout), `CONTRIBUTING.md` (shim rule, gates, conventions),
  `AGENTS.md` (session constraints, verification, working agreement),
  `.github/workflows/ci.yml` (windows-latest, .NET 10 SDK, build + test the
  slnx) and `.github/PULL_REQUEST_TEMPLATE.md` (outcome-first, linkage,
  verification checklist). No engine changes; suite stays 265+23 green.
  License choice stays explicitly open (README records "undecided").
- Step 27 (done): Benchmark harness + in-repo regression probes (review plan item 8, first slice).
  `benchmarks/bench.py` serves the app (or takes `--url`), tours all 15 demos
  headless with a fresh profile per run (render proof via canvas dataURL,
  loadMs informational, 2 s rAF FPS sample, console/page error capture) plus
  scripted menu open/invoke and dialog open/accept interactions, writes
  timestamped JSON to gitignored `benchmarks/results/`, and compares against
  committed `benchmarks/baselines/latest.json` (exit 0 pass / 1 fail / 2 no
  baseline; FPS gates at 70% of baseline; blanks/errors/interactions are hard
  fails). `benchmarks/run.ps1` = build + bench; `requirements.txt` pins
  playwright>=1.49 (`sync_api` import — the old `sync_playwright` shim is
  gone in current installs). `shots/` holds reference screenshots next to
  the harness; results never commit. `BaselineTests` keeps the committed
  baseline honest (schema, full slug coverage, sane metrics, no errors) so a
  new demo without `--update-baseline` fails `dotnet test`. Native Windows
  baselines stay future work. 1 new test file (+1 test); full suite 265+23
  green. 15/15 demos green through the harness itself with reference shots.
- Step 44 (done): Browser-gate reconcile (no engine surface; packaging honesty). Geometry re-packaged live with the current converter/engine: converted, 1 assembly, 0 gaps, sourceCommit pinned to the catalog commit (prior staged manifest carried sourceCommit null and an orphaned image, both removed); boot still stops loud at Application.Resources so the catalog stays not-ready with that boundary. The 8 stale conversion-failed manifests re-packaged live (fetch+build+package, current engine): avalondock 251 -> 125, avalonedit 273 -> 172, handycontrol 749 -> 457, ilspy 1142 -> 632, mahapps 1167 -> 664 (2 -> 3 assemblies), material-design 568 -> 315 (2 -> 3), oxyplot 152 -> 65, pc-space-map 74 -> 38; orphaned images pruned, ledger and per-app build logs refreshed. Compatibility report rows + top-16 list regenerated from live manifests (same shape, prose untouched). New GeometryManifestCatalogConsistencyTests pins manifest-converted/gaps-empty/commit-pinned plus catalog-unready-with-Application.Resources-boundary. BENCH PASS: 22 demos, 6 interactions, 15/15 launcher checks, no errors (benchmarks/results/20261023T071425Z.json). Full suite 480 Wpf (incl. 2 new) + 11 Convert + 30 Core green, Node 14/14; port 5199 confirmed free.
- Step 42 (done): Imaging v1 (compat slice; gallery 53 -> 48 gaps, screen-to-gif 423 -> 344). Engine ImageSource/BitmapSource pipeline with managed RGBA buffers: BitmapSource.Create + CopyPixels, BitmapImage (StreamSource/UriSource via .g.resources pack lookup, sync decode through Skia, DownloadCompleted), BitmapFrame/BitmapDecoder/BitmapEncoder (+PNG/JPEG/hand-written BMP) and GifBitmapDecoder, WriteableBitmap (WritePixels/Lock/Unlock/pinned BackBuffer/AddDirtyRect), CroppedBitmap/FormatConvertedBitmap/TransformedBitmap (nearest-neighbor), RenderTargetBitmap (UIElement/DrawingVisual raster via headless + Skia readback), BitmapPalette/PixelFormat(s)/BitmapCacheOption/BitmapCreateOptions/BitmapSizeOptions/BitmapMetadata/ImageMetadata. UIElement now derives Visual (real hierarchy); DrawingVisual + RenderOpen, VisualTreeHelper, full Matrix/Transform stack (Translate/Rotate/Scale/Matrix/Group), RenderOptions/BitmapScalingMode/EdgeMode, Stretch, Image control with aspect-correct layout. XAML: Image Source/Stretch, nested BitmapImage, BitmapImage resources, ImageSource StaticResource. Engine DrawingContext.DrawImage (op 7) renders natively and in the browser (new wasm-host.js case 7 with decoded-image cache). Converted ConvertPipelineTests updated to the Visual-maps contract (negative fixture now MediaPlayer). 19 new ImagingTests; full suite 450 Wpf + 10 Convert + 30 Core green, Node 14/14.

- Step 43 (done): Items controls cluster (compat slice; gallery 48 -> 31 gaps, screen-to-gif 344 -> 310). New engine surface, all member-evidenced from converted gallery/gif refs: ItemsControl (Items/ItemsSource/DisplayMemberPath/AlternationCount, AddItem, Label containers via ItemContainerGenerator with ContainerFromItem/Index/ItemFromContainer, ItemsControlFromItemContainer), Selector (SelectedIndex/SelectedItem/SelectedValue/SelectedValuePath/SelectionMode/IsSynchronizedWithCurrentItem, typed SelectionChangedEventArgs/Handler with AddedItems/RemovedItems, raw pending index that materializes, pre-selected row honoring, row IsSelected two-way sync), HeaderedItemsControl.Header, ComboBox (ComboBoxItem rows with Content/IsSelected, header + drop-down panel hit-testing, press-toggle/press-select, Escape/Arrow keyboard, Text/IsEditable/MaxDropDownHeight), ListView/ListViewItem (Selector selection), CollectionView/ListCollectionView (currency, Refresh, PropertyChanged, GroupDescriptions), BindingBase (FallbackValue/TargetNullValue/StringFormat, honored past the converter) with Binding.Converter/ConverterParameter/ConverterCulture/ElementName/UpdateSourceTrigger/DoNothing (trigger honored on editor auto-push after the UpdateSources fix), IValueConverter, GroupDescription/PropertyGroupDescription, Orientation (StackPanel.Horizontal layout), ResizeMode/WindowStyle/WindowState/WindowStartupLocation/Topmost/ShowInTaskbar (parsed window chrome), SystemParameters, FrameworkElement.FindName (root-up namescope walk). XAML: new tags (ItemsControl/HeaderedItemsControl/ComboBox/ComboBoxItem/ListView/ListViewItem), child/text/property-element attachment, full Selector/ComboBox/Window-chrome/StackPanel attribute domains, ItemsSource-literal and Converter-resource precise errors, ElementName/FallbackValue/TargetNullValue/StringFormat/UpdateSourceTrigger binding options. BAML: Items-cluster types by full CLR name (no invented well-known ids), SelectedIndex/Text/drop/DisplayMemberPath members, Items/default content attach, Content/Header member branches. Convert WpfTypeMap probes extended (MapCoversItemsCluster). Legacy ListBox/TreeView/DataGrid keep their row models and Action-typed SelectionChanged untouched (unification deferred until subscribers migrate). Two outdated boundary tests updated to the new contract (horizontal StackPanel, ElementName/trigger binding options). 27 new ItemsTests (+1 Convert, +1 Binding); full suite 478 Wpf + 11 Convert + 30 Core green, Node 14/14 (studio suite re-run).

- Step 41 (done): Control + styling + fonts (compat slice; gallery 69 -> 53 gaps, screen-to-gif 459 -> 423). New engine surface, all member-evidenced from converted gallery/gif refs: Control (Brush/Font/Template/MouseDoubleClick DPs + props) with UserControl and TextBoxBase (TextChangedEvent/TextChangedEventHandler/UndoAction, IsReadOnly, SelectAll/selection, OnTextChanged); Style/Setter/SetterBase/SetterBaseCollection/EventSetter with BasedOn ordering, sealing, CLR-or-DP resolution, attached setters (DockPanel.Dock et al.) and XAML EventSetter wires; RoutedEvent/RoutingStrategy/EventManager + UIElement AddHandler/RemoveHandler/RaiseEvent (Tunnel/Bubble/Direct, handledEventsToo) and protected On* input/focus virtuals; ButtonBase.ClickEvent/MenuItem.ClickEvent tokens; Button double-click detection; TextBlock/ListBoxItem/TreeViewItem/DataGrid/MenuItem Brush-typed Foreground (+TextBlock Background); FontFamily/FontStyle/FontWeight/FontStyles/FontWeights (incl. SemiBold), ThemeMode, ResourceKey, markup attributes, ControlTemplate shell, SystemColors core; CornerRadius struct (Border renders TopLeft). XAML: real Style objects replace LiteralStyle (implicit/explicit/property-element/{StaticResource}), typed Control/Font/Brush attributes, BorderThickness separate from Thickness. 33 new StylingTests; full suite 431 Wpf + 10 Convert + 30 Core green, Node 14/14.

- Step 40 (done): Routed commands. RoutedCommand/RoutedUICommand with preview/bubble routing through element + class CommandBindings (target-first bubble order), CommandManager (RequerySuggested, InvalidateRequerySuggested, Add/RemovePreviewCanExecuteHandler) and full stock commands (ApplicationCommands, ComponentCommands, NavigationCommands, MediaCommands, EditingCommands, SystemCommands chrome verbs); UIElement.CommandBindings collection; ButtonBase/MenuItem Command/CommandParameter/CommandTarget with target-aware auto re-query (focused-element default target, CommandTarget setter re-queries, click routes from target). 13 new RoutedCommandTests (bubble first-from-target order, preview-then-bubble, stock identity, veto greys button, target routing); WpfTypeMap probe extended with the four new stock classes. Re-packaged gallery 99 -> 69 gaps and screen-to-gif 603 -> 459 gaps (remaining gif Input gaps are stylus-only). Full suite 398 Wpf (incl. 13 new) + 10 Convert + 30 Core green.

- Step 26 (done): Pack URIs + resources/dictionaries (review plan item 3, first slice).
  `ResourceDictionary` (local then merged-in-order lookup, duplicate-key errors),
  `PackUri` (pack://application/siteoforigin with Assembly;component, relative
  paths; normalized case-insensitive keys; precise errors), `Application`
  (Current, Resources, in-memory pack store filled by RegisterPackResource —
  hosts fetch into the same store later). `FrameworkElement.Resources` +
  `TryFindResource` (element, ancestors, application). XAML:
  `<Window.Resources>` (any FrameworkElement, single dictionary, replaces),
  entries (SolidColorBrush/Color/String/Double/Int32/Boolean with x:Key,
  precise errors), `<ResourceDictionary.MergedDictionaries>` with
  `Source=` pack loading (depth guard against cycles),
  `{StaticResource Key}` attribute AND property-element forms. StaticResource
  resolves through the loader's lexical scope stack at attach time (Parent
  links do not exist yet when attributes parse) — same-scope forward
  references still fail like WPF; root self-references are leniently allowed.
  Resolved values convert through the existing property domains (color/bool/
  double/string); `{DynamicResource}` and resource complex properties in BAML
  (0x07) stay precise errors. 19 new tests; full suite 264+23 green. New
  "resources" demo (inline dictionary + Theme.xaml pack merge, StaticResource
  text/colors/sizes); 15/15 demos render headless. Styles, templates, themes,
  triggers and DynamicResource re-resolution stay out of scope.
- Step 25 (done): Dialogs + docking (review plan item 6, sixth slice).
  `DockPanel` with edge docking in child order (Left/Right vertical slabs,
  Top/Bottom horizontal slabs; undocked defaults to Left), `LastChildFill`
  (default true), margins honored, WPF accumulate/max measure shape
  (natural size may exceed available; arrange clamps). `GetDock`/`SetDock`
 /`TryParseDock` (string-backed attached values read through the same
  path); `Canvas.SetLeft/SetTop` added for parity. XAML: `<DockPanel>`,
  `DockPanel.Dock="..."` (precise error on bad values), `LastChildFill`.
  BAML: -163 (DockPanel) verified against a real fixture via out-of-repo
  probe, children via Panel.Children -213, `Dock` -39 (EnumConverter) and
  `LastChildFill` -40 (BooleanConverter, like IsEnabled). Modal dialogs:
  `WpfInputManager.ShowDialog/CloseDialog` (auto-roots, visibility, focus;
  `ModalRoot` gates pointer/wheel/keyboard routes, Tab order and
  programmatic focus to the dialog; focus parked inside clears on close),
  `Window` gains `IsVisible` (hidden windows skip render and routing),
  `DialogResult` (plain, no auto-close magic), `IsModal`, `Closed`,
  `Close()`; `WasmWpfHost.ShowDialog/CloseDialog` + Tick skips hidden.
  23 new tests; full suite 245+23 green. New "dialogs" demo (DockPanel
  toolbar/status/fill + dimmed modal confirm dialog, OK/Cancel results in
  the status line; host hook wires dialog buttons) — 14/14 demos render
  headless with live open/OK/close verified by screenshot. Non-modal
  multi-window Z-order/DPI, dialog Escape-to-cancel and result auto-close
  stay out of scope.
- Step 24 (done): Menu/menus+popups (review plan item 6, fifth slice).
  `Menu` bar of `MenuItem` entries with dropdown submenus. Press toggles,
  hover highlights and swaps the open submenu, release-inside invokes
  (drag-across works: release over any enabled leaf fires), Escape,
  focus-loss and release-outside-after-press-inside close; presses on dead
  background with no prior press never reach the menu (input routes to the
  hit target only), so that light-dismiss path stays a v1 gap. Full
  keyboard nav when focused (arrows/Home/End/Enter/Space, skipping disabled
  entries). Entry input: `Click`, `Command`/`CommandParameter` (CanExecute
  gates execution AND greys the entry, like Button), `IsEnabled`,
  `SubmenuOpened/Closed` on toggle. Layout: bar defines size (28px,
  horizontal), panels overlay via Menu-owned `DropRect`s arranged
  post-bar (clamped to the window). Panels are owned overlays: a new
  `UIElement.HitTestOverlay` virtual (Menu returns the open top panel)
  is consulted at each parent BEFORE the sibling walk, so a panel wins
  over content arranged beneath it while the owner stays in the route
  (full chain for tunneling/bubbling/focus); `HitTestRecursive` matches.
  Rendering: bar band + bottom rule, panel fill + border, blue highlight,
  grey ink for command/disabled-blocked, `>` arrows; open panels paint in
  a `WasmMilCore.RenderOverlays` post-pass after the whole tree (above
  later siblings, unclipped by ancestors — popup-like without a separate
  visual layer). XAML:
  `<Menu><MenuItem Header= ...>` with `Header` text/property-element,
  `IsEnabled`, `Foreground`, `FontSize`; non-MenuItem children are precise
  errors. BAML: -401 (Menu) / -403 (MenuItem) verified against a real Menu
  fixture via out-of-repo probe (new dumper mirroring engine framing;
  old walker choked on 0x14), reusing ItemsControl.Items -192 and
  HeaderedItemsControl.Header -71; IsEnabled -133 + BooleanConverter -46
  mapped (bool.TryParse domain, like IsExpanded). 18 new tests; full suite
  222+23 green. New "menu" demo (File/Edit/Help, command-greyed Save,
  disabled Redo, live status line); 13/13 demos render headless.
  Separators, check-state/icons, cascading placement and light-dismiss on
  dead background stay out of scope.
- Step 23 (done): DataGrid table (review plan item 6, fourth slice).
  `DataGrid` with `DataGridTextColumn` columns (Header/BindingPath/Width;
  `BindingPath` is an engine extension — real WPF takes a Binding object,
  which needs markup-extension support and stays a precise BAML boundary).
  Plain-CLR rows in an observable `Items` list (INotifyCollectionChanged
  re-measures and tracks selection: shifts, materialization, reset-clear);
  cells read via the binding engine's property reader (missing paths render
  blank). Pixel/star/auto columns (star shares the inner width; auto fits
  header+cells), header band + gridlines, blue-highlight/white-ink row
  selection (greyed when disabled), overflow clip (no internal scroller).
  Selector semantics mirrored from ListBox: SelectedIndex with pending
  values, SelectedItem by reference, SelectionChanged on effective change;
  press-to-select, focus-gated arrows/Home/End, disabled grids ignore input.
  XAML: `<DataGrid><DataGrid.Columns><DataGridTextColumn .../>` parsed by a
  dedicated reader (mirrors grid definitions; non-visual columns never go
  through CreateElement); direct column children and bad widths/attrs are
  precise errors. BAML: two framework firsts — DataGrid/DataGridTextColumn
  have NO well-known short ids (verified against a real fixture: they travel
  by TypeInfo name, mapped strictly by full CLR name in
  PresentationFramework) and `<Columns>` arrives as PropertyIListStart 0x0B
  + U16 attribute id / bare PropertyIListEnd 0x0C (framing verified by
  hexdump: `0B <id-lo> <id-hi>`, bare `0C`). Mapped: SelectedIndex per-file
  attribute, Header (StringConverter), Width by DataGridLengthConverter
  TypeInfo (any converter, self-validating parse). The probe confirmed
  SelectedIndex compiles on real DataGrid (Selector parity, unlike TreeView).
  15 new tests; full suite 204+23 green. New "datagrid" demo (employees
  table, Grace pre-selected); 12/12 demos render headless with live
  click-select confirmed by screenshot. Cell editing, sorting, grouping,
  cell/column selection, auto-generated columns, virtualization and rows in
  markup stay out of scope.
- Step 22 (done): TreeView hierarchy (review plan item 6, third slice).
  `TreeView`/`TreeViewItem` nodes with expand/collapse, +/- box expanders
  (rectangles + ASCII text only — the Canvas2D transport skips geometry ops
  and triangle-glyph coverage cannot be assumed), indent guides by depth,
  overflow clip (no internal scroller — nest in a ScrollViewer), and
  ListBox-style selection highlight. Selection is a node reference
  (`SelectedItem`, hidden nodes included, detached nodes clear); there is
  deliberately no `SelectedIndex` — real WPF rejects it at compile time
  (MC3072: TreeView derives from ItemsControl, not Selector), and the
  engine's XAML rejects it the same way (covered by test). Input: chevron
  gutter toggles, press-elsewhere selects, focus-gated arrows/Home/End plus
  Right (expand/descend) and Left (collapse/parent), disabled trees ignore
  input; Expanded/Collapsed events on nodes. Removal detaches cleanly
  (selection clears with an event, including nested removes). XAML:
  `<TreeView>`/`<TreeViewItem>`, Header/IsExpanded/Foreground/FontSize,
  inner text, non-item children are precise errors. BAML: -686/-687
  verified against a real TreeView fixture via out-of-repo probe, plus
  HeaderedContentControl.Header member -71 and TreeViewItem.IsExpanded as a
  per-file attribute (any bool converter — the value domain is
  self-validating). The probe's `SelectedIndex` rejection parity check
  confirmed the no-index design before any engine code relied on it.
  15 new tests; full suite 189+23 green. New "treeview" demo (docs/src
  hierarchy, faq.md pre-selected); 11/11 demos render headless with live
  click-select and chevron-toggle repaints confirmed by screenshot.
  Virtualization, multi-selection, data templating and drag-reorder stay
  out of scope.
- Step 21 (done): ListBox item containers + selection (review plan item 6,
  second slice). `ListBox` (ListBoxItem rows only, enforced at loader
  boundaries and programmatically) with StackPanel slot layout, 1px border
  chrome, overflow clip (no internal scroller — nest in a ScrollViewer),
  and blue-highlight/white-ink selection (greyed when disabled). Selector
  subset: SelectedIndex (raw value kept, effective index -1 while beyond
  the rows — markup attributes arrive before children, as in real WPF),
  SelectedItem (reference set, foreign item deselects), SelectionChanged on
  any effective index/item change. Press-to-select, focus-gated
  ArrowUp/Down/Home/End, disabled lists ignore input. Removal tracks the
  item: rows above shift the index, the selected row hands off to the row
  sliding in (-1 past the end). XAML: `<ListBox>`/`<ListBoxItem>`,
  Content/Foreground/FontSize, SelectedIndex, inner text, non-item child is
  a precise error. BAML: -373/-374 verified against a real ListBox fixture
  via out-of-repo probe, plus ItemsControl.Items member -192 and
  Selector.SelectedIndex as a per-file attribute (any int32 converter — the
  value domain is self-validating); the probe caught a real bug (attribute
  coerced to -1 before rows attached) fixed by pending-selection semantics,
  re-verified (items=2 sel=1 selItem=b). 14 new tests; full suite 174+23
  green. New "listbox" demo (6 fruits, Cherry pre-selected); 10/10 demos
  render headless with live click-select repaint confirmed by screenshot.
  Virtualization, multi-selection and ItemsSource/ItemsControl templating
  stay out of scope.
- Step 20 (done): ScrollViewer/scroll (review plan item 6, first slice).
  `ScrollViewer` control (single content child, vertical only): measure
  reserves a 12px always-on scrollbar gutter and measures content at
  unconstrained height (extent); arrange clips the viewport via Push/PopClip
  in both transports and shifts the child by -VerticalOffset, so existing
  arranged-rect hit-testing keeps working through scroll. Input: line wheel
  (focus-free, hover-routed, disabled-gated), thumb press-drag-release,
  keyboard (arrows/PageUp/PageDown/Home/End, focus-gated like TextBox).
  `ScrollChanged` fires on real changes; offset clamps silently when content
  shrinks. XAML: `<ScrollViewer>` + direct child (second child is a precise
  error); `VerticalOffset=` parses (loose-XAML convenience — real WPF keeps
  it read-only, so real BAML can never carry it). BAML: -550 verified
  against a real ScrollViewer fixture via out-of-repo probe (reflection
  found no id; the engine's own "unknown id" error at the fixture's
  `<ScrollViewer>` line gave -550, cross-checked against the desktop node
  stream showing ContentControl.Content + TextBlock.Text), plus TextBlock
  text member -110 discovered the same way. 11 new tests (layout/extent,
  wheel, disabled gating, focus-gated keys, drag math mirrored to 1e-6,
  scrolled hit-testing, XAML, synthetic BAML); full suite 160+23 green.
  New "scroll" demo (20 rows; header selection proves hit-testing through
  scroll); 9/9 demos render headless with live wheel repaint confirmed by
  screenshot. Pre-existing note: Blazor's document-level wheel delegation
  logs a passive-listener preventDefault warning on any wheel (Home.razor
  predates this step; page shell is overflow:hidden so it is cosmetic).
- Step 19 (done): ICommand/CanExecute + IsEnabled. `ICommand` interface and
  `RelayCommand` (parameter + parameterless forms, RaiseCanExecuteChanged)
  with zero WindowsBase dependency; `Button.Command`/`CommandParameter`
  (executes on click only while CanExecute holds; Click still fires too);
  `UIElement.IsEnabled` + virtual `IsEffectivelyEnabled` (buttons AND the
  command gate) honored by TryFocus, press-focus, Tab order, button press
  and TextBox keys; command-blocked/disabled buttons render greyed.
  XAML: `CommandParameter=` plain value, `Command="{Binding}"` via the
  existing binding path, literal `Command=` is a precise namescope error;
  BAML commands stay precise-error records. 14 new tests; full suite
  150+23 green. Binding demo's Birthday button is now a command that
  greys itself out at Age 40: 8/8 demos render with zero console/page
  errors, 4 live clicks repaint (36→40) and clicks 5-6 are silent,
  confirmed headless and by screenshot.
- MD templated-list paint (2026-10-30, wpf-apps): `ListBox` with a set
  `ControlTemplate` now arranges its chrome and still paints generated rows
  (new `ListBoxTests.Templated_list_paints_rows_through_chrome`, red-to-green).
  MD drawer rows emit ink (browser frame: 102 ops incl 37 black DrawTexts,
  balanced clips), but 3 trailing fullscreen #f1f1f1 fills bury them, so first
  paint stays white. Next: identify the overlay nodes (DialogHost/DrawerHost
  cascade) and fix visibility/brush, then MD navigate/edit/command.
- MD zero-opacity skip (2026-10-30, wpf-apps): overlay nodes identified as
  DrawerHost drawer shadows at Opacity 0 (VSM Closed states entered, fade
  storyboards run); `RenderVisible` now skips Opacity<=0 elements and the
  staged first frame ends with row ink (2 new drawer paint tests red-to-green).
  Full-suite recount blocked by an early testhost hang (x2, pids cleaned); then
  rebuild/re-serve and browser navigate/edit/command proof.
