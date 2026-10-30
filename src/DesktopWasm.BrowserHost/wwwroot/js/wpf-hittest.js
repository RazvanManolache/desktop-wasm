// Status channel for the hit-test lab (DynamicResource + hit-test slice).
// The lab mirrors every readout into status text and publishes logical
// rects after layout, so the proof asserts post-input state instead of
// repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
window.wpfHitTest = window.wpfHitTest || {};
window.wpfHitTest.status = window.wpfHitTest.status || {};

window.wpfHitTest.note = function (key, value) {
    window.wpfHitTest.status[key] = value;
};

window.wpfHitTest.attach = function () {
    window.wpfHitTest.status = {};
    return true;
};

window.wpfHitTest.detach = function () {
    window.wpfHitTest.status = {};
};
