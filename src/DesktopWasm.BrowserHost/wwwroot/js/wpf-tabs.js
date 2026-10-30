// Status channel for the tabs lab (ScreenToGif TabControl/ProgressBar/
// ToolTip slice). The lab mirrors every readout into status text and
// publishes logical rects after layout, so the proof asserts post-input
// state instead of repaint hashes alone. No input bridging: Playwright
// drives the real pointer path through the shared input surface.
window.wpfTabs = window.wpfTabs || {};
window.wpfTabs.status = window.wpfTabs.status || {};

window.wpfTabs.note = function (key, value) {
    window.wpfTabs.status[key] = value;
};

window.wpfTabs.attach = function () {
    window.wpfTabs.status = {};
    return true;
};

window.wpfTabs.detach = function () {
    window.wpfTabs.status = {};
};
