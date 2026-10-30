// Status channel for the text lab (ScreenToGif typeface/decoration/DPI
// slice). The lab mirrors every readout into status text and publishes
// logical rects after layout, so the proof asserts post-input state instead
// of repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
window.wpfText = window.wpfText || {};
window.wpfText.status = window.wpfText.status || {};

window.wpfText.note = function (key, value) {
    window.wpfText.status[key] = value;
};

window.wpfText.attach = function () {
    window.wpfText.status = {};
    return true;
};

window.wpfText.detach = function () {
    window.wpfText.status = {};
};
