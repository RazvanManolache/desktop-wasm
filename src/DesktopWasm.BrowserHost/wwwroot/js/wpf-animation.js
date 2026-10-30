// Status channel for the animation lab (ScreenToGif HideableTabControl
// show/hide slice). The lab mirrors every readout into status text and
// publishes logical rects after layout, so the proof asserts interpolated
// post-click state instead of repaint hashes alone. No input bridging:
// Playwright drives the real pointer path through the shared input surface.
window.wpfAnimation = window.wpfAnimation || {};
window.wpfAnimation.status = window.wpfAnimation.status || {};

window.wpfAnimation.note = function (key, value) {
    window.wpfAnimation.status[key] = value;
};

window.wpfAnimation.attach = function () {
    window.wpfAnimation.status = {};
    return true;
};

window.wpfAnimation.detach = function () {
    window.wpfAnimation.status = {};
};
