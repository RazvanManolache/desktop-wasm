// Status channel for the primitives lab (ScreenToGif Thumb/RepeatButton/
// Popup slice). The lab mirrors every readout into status text and publishes
// logical rects after layout, so the proof asserts post-input state instead
// of repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
window.wpfPrimitives = window.wpfPrimitives || {};
window.wpfPrimitives.status = window.wpfPrimitives.status || {};

window.wpfPrimitives.note = function (key, value) {
    window.wpfPrimitives.status[key] = value;
};

window.wpfPrimitives.attach = function () {
    window.wpfPrimitives.status = {};
    return true;
};

window.wpfPrimitives.detach = function () {
    window.wpfPrimitives.status = {};
};
