// Status channel for the shell/windowing lab (ScreenToGif shell slice).
// The lab mirrors every readout into status text and publishes logical
// rects after layout, so the proof asserts post-input state instead of
// repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
window.wpfShell = window.wpfShell || {};
window.wpfShell.status = window.wpfShell.status || {};

window.wpfShell.note = function (key, value) {
    window.wpfShell.status[key] = value;
};

window.wpfShell.attach = function () {
    window.wpfShell.status = {};
    return true;
};

window.wpfShell.detach = function () {
    window.wpfShell.status = {};
};
