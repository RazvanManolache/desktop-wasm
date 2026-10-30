// Status channel for the real converted WPFGallery (external app).
// The gallery mirrors navigation + control state into status text and
// logical rects after layout, so the proof asserts post-input STATE
// instead of repaint hashes alone. No input bridging: Playwright drives
// the real pointer path through the shared input surface.
window.wpfGallery = window.wpfGallery || {};
window.wpfGallery.status = window.wpfGallery.status || {};

window.wpfGallery.note = function (key, value) {
    window.wpfGallery.status[key] = value;
};

window.wpfGallery.attach = function () {
    window.wpfGallery.status = {};
    return true;
};

window.wpfGallery.detach = function () {
    window.wpfGallery.status = {};
};

window.wpfGallery.refresh = function () {
    if (window.DotNet) return window.DotNet.invokeMethodAsync('DesktopWasm.BrowserHost', 'RefreshNotes');
    return Promise.resolve(false);
};
