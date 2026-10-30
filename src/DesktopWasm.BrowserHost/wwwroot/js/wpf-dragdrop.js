// Real HTML5 file-drop bridge for the drag-drop lab (ScreenToGif
// drag-drop/data-object slice). The editor's Control_Drop reads a FileDrop
// string array from the routed event; the browser's DataTransfer.files are
// the only source of those paths, so this listener stages real dropped
// bytes and hands them to .NET, which mounts them and raises the same
// routed enter/drop the simulated buttons drive. No synthetic payloads:
// an empty or unreadable drop reports through the lab readout.
window.wpfDragDrop = window.wpfDragDrop || {};
window.wpfDragDrop.status = window.wpfDragDrop.status || {};

window.wpfDragDrop.note = function (key, value) {
    window.wpfDragDrop.status[key] = value;
};

window.wpfDragDrop.attach = function (elementId, dotNet) {
    window.wpfDragDrop.detach(elementId);
    const el = document.getElementById(elementId);
    if (!el || !dotNet) return false;
    const over = (e) => { e.preventDefault(); };
    const drop = (e) => {
        e.preventDefault();
        const files = [...(e.dataTransfer?.files || [])].slice(0, 4);
        Promise.all(files.map((f) => new Promise((resolve) => {
            if (f.size > 8 * 1024 * 1024) {
                resolve({ name: f.name, tooLarge: true, bytesBase64: "" });
                return;
            }
            const reader = new FileReader();
            reader.onload = () => resolve({
                name: f.name,
                bytesBase64: String(reader.result || "").split(",")[1] || "",
            });
            reader.onerror = () => resolve({ name: f.name, tooLarge: true, bytesBase64: "" });
            reader.readAsDataURL(f);
        }))).then((list) =>
            dotNet.invokeMethodAsync("OnBrowserFilesDropped", { files: list }).catch(() => {}));
    };
    el.addEventListener("dragover", over);
    el.addEventListener("drop", drop);
    el._wpfDrop = { over, drop };
    return true;
};

window.wpfDragDrop.detach = function (elementId) {
    const el = document.getElementById(elementId);
    if (el && el._wpfDrop) {
        el.removeEventListener("dragover", el._wpfDrop.over);
        el.removeEventListener("drop", el._wpfDrop.drop);
        el._wpfDrop = null;
    }
};
