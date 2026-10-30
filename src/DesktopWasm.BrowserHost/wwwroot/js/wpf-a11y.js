// Browser mediation for the announcements & preferences lab (engine-native
// Gallery automation slice). Every announcement paints in the engine
// readout first; window.wpfA11y.status mirrors the latest text for
// automation introspection, and the aria-live region voices it.
window.wpfA11y = window.wpfA11y || {};
window.wpfA11y.status = window.wpfA11y.status || { text: "", activityId: "" };

window.wpfA11y.announce = function (text, activityId) {
    window.wpfA11y.status.text = text || "";
    window.wpfA11y.status.activityId = activityId || "";
    var live = document.getElementById("wpf-a11y-live");
    if (live) {
        live.textContent = "";
        live.textContent = text || "";
    }
    return "ok";
};
