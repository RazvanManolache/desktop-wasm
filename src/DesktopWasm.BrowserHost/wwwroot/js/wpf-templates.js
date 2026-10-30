// Status channel for the templates lab (ControlTemplate/DataTemplate
// inflation slice). The lab mirrors every readout into status text and
// publishes logical rects after layout, so the proof asserts post-input
// state instead of repaint hashes alone. No input bridging: Playwright
// drives the real pointer path through the shared input surface.
window.wpfTemplates = window.wpfTemplates || {};
window.wpfTemplates.status = window.wpfTemplates.status || {};

window.wpfTemplates.note = function (key, value) {
    window.wpfTemplates.status[key] = value;
};

window.wpfTemplates.attach = function () {
    window.wpfTemplates.status = {};
    return true;
};

window.wpfTemplates.detach = function () {
    window.wpfTemplates.status = {};
};
