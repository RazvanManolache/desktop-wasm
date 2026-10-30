// Status channel for the real converted OxyPlot ExampleBrowser (external app).
// The browser mirrors example-list + plot-host state into status text and
// logical rects after layout, so the proof asserts post-input STATE
// instead of repaint hashes alone. No input bridging: Playwright drives
// the real pointer path through the shared input surface.
window.wpfOxyplot = window.wpfOxyplot || {};
window.wpfOxyplot.status = window.wpfOxyplot.status || {};

window.wpfOxyplot.note = function (key, value) {
    window.wpfOxyplot.status[key] = value;
};

window.wpfOxyplot.attach = function () {
    window.wpfOxyplot.status = {};
    return true;
};

window.wpfOxyplot.detach = function () {
    window.wpfOxyplot.status = {};
};

window.wpfOxyplot.refresh = function () {
    if (window.DotNet) return window.DotNet.invokeMethodAsync('DesktopWasm.BrowserHost', 'RefreshOxyPlotNotes');
    return Promise.resolve(false);
};
