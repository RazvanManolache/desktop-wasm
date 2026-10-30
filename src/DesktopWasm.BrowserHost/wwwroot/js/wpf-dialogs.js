// Browser mediation for the files & clipboard lab (engine-native Gallery
// FileAndFolderDialogsPage/ClipboardPage slice). Every helper reports a
// real outcome: user cancellation resolves {cancelled:true}, clipboard or
// picker denial rejects (the .NET side paints it), and staged bytes are the
// user's actual file content. window.wpfFileDialogs.status mirrors painted
// readouts for automation introspection.
window.wpfFileDialogs = window.wpfFileDialogs || {};
window.wpfFileDialogs.status = window.wpfFileDialogs.status || {};

window.wpfFileDialogs.note = function (key, value) {
    window.wpfFileDialogs.status[key] = value;
    return "ok";
};

window.wpfFileDialogs.readClipboardText = async function () {
    return await navigator.clipboard.readText();
};

window.wpfFileDialogs.writeClipboardText = async function (text) {
    await navigator.clipboard.writeText(text);
    return "ok";
};

function wpfFileDialogsBytes(file) {
    return file.arrayBuffer().then(buffer => {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        const chunk = 8192;
        for (let i = 0; i < bytes.length; i += chunk)
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        return { name: file.name, size: file.size, bytesBase64: btoa(binary) };
    });
}

window.wpfFileDialogs.pickFiles = function (options) {
    const multiple = !!(options && options.multiple);
    const accept = (options && options.accept) || "";
    return new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.style.display = "none";
        if (multiple) input.multiple = true;
        if (accept) input.accept = accept;
        let done = false;
        const finish = value => {
            if (done) return;
            done = true;
            if (input.parentNode) input.parentNode.removeChild(input);
            resolve(value);
        };
        input.addEventListener("change", async () => {
            try {
                const files = [];
                for (const file of input.files || []) files.push(await wpfFileDialogsBytes(file));
                finish({ cancelled: false, files });
            } catch (error) {
                finish({ cancelled: true, error: String(error && error.message || error) });
            }
        });
        input.addEventListener("cancel", () => finish({ cancelled: true }));
        document.body.appendChild(input);
        input.click();
    });
};

window.wpfFileDialogs.pickFolder = function () {
    return new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.style.display = "none";
        input.setAttribute("webkitdirectory", "");
        let done = false;
        const finish = value => {
            if (done) return;
            done = true;
            if (input.parentNode) input.parentNode.removeChild(input);
            resolve(value);
        };
        input.addEventListener("change", async () => {
            try {
                const files = [];
                let folder = "";
                for (const file of input.files || []) {
                    const relative = file.webkitRelativePath || file.name;
                    if (!folder && relative.indexOf("/") >= 0) folder = relative.slice(0, relative.indexOf("/"));
                    files.push(Object.assign({ relative }, await wpfFileDialogsBytes(file)));
                }
                finish({ cancelled: files.length === 0, folder, files });
            } catch (error) {
                finish({ cancelled: true, error: String(error && error.message || error) });
            }
        });
        input.addEventListener("cancel", () => finish({ cancelled: true }));
        document.body.appendChild(input);
        input.click();
    });
};

window.wpfFileDialogs.saveFile = async function (options) {
    const name = (options && options.suggestedName) || "download.bin";
    const bytesBase64 = (options && options.bytesBase64) || "";
    const binary = atob(bytesBase64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    const url = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" }));
    try {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = name;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    } finally {
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    }
    return { saved: true, name, size: bytes.length };
};
