// Status channel for the effects/hosts lab (ScreenToGif effects/host slice).
// The lab mirrors every readout into status text and publishes logical
// rects after layout, so the proof asserts post-input state instead of
// repaint hashes alone. No input bridging: Playwright drives the real
// pointer/keyboard path through the shared input surface.
window.wpfEffects = window.wpfEffects || {};
window.wpfEffects.status = window.wpfEffects.status || {};

window.wpfEffects.note = function (key, value) {
    window.wpfEffects.status[key] = value;
};

window.wpfEffects.attach = function () {
    window.wpfEffects.status = {};
    return true;
};

window.wpfEffects.detach = function () {
    window.wpfEffects.status = {};
};
