// Status channel for the grid-rows lab (ScreenToGif DataGrid row/cell
// slice). The lab mirrors every readout into status text and publishes
// logical rects after layout, so the proof asserts post-input state instead
// of repaint hashes alone. No input bridging: Playwright drives the real
// pointer path through the shared input surface.
window.wpfGridRows = window.wpfGridRows || {};
window.wpfGridRows.status = window.wpfGridRows.status || {};

window.wpfGridRows.note = function (key, value) {
    window.wpfGridRows.status[key] = value;
};

window.wpfGridRows.attach = function () {
    window.wpfGridRows.status = {};
    return true;
};

window.wpfGridRows.detach = function () {
    window.wpfGridRows.status = {};
};
