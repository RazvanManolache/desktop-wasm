// Canvas2D host bridge for DesktopWasm.Wpf frames.
// Same op protocol as the WinForms canvas-bridge.js:
// 1 = fillRect [x,y,w,h,css], 2 = roundRect [x,y,w,h,r,css],
// 3 = fillText [text,x,y,fontCss,css,hAlign,vAlign,mnemonic],
// 4 = fillGradientV [x,y,w,h,topCss,bottomCss],
// 5 = strokeLine [x1,y1,x2,y2,width,css],
// 6 = ellipse [x,y,w,h,lineWidth,outlineCss,fillCss|null],
// 7 = drawImage [x,y,dw,dh,w,h,base64Rgba],
// 9 = blitBitmap [layerId,x,y,dw,dh], 10 = retainLayers [liveIds],
// 12/13 = pushClip/popClip, 14/15 = pushTransform/popTransform.
// 8/11 (bitmap uploads) travel via texDef/texPatch; 16 (geometry) is skipped.
window.wfwasm = window.wfwasm || {};
window.wfwasm._layers = {};
window.wfwasm._images = window.wfwasm._images || new Map();

function wfwasmBytes(base64) {
    var bin = atob(base64);
    var bytes = new Uint8ClampedArray(bin.length);
    for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
}

window.wfwasm.texDef = function (layerId, base64Rgba, width, height) {
    window.wfwasm._layers[layerId] = new ImageData(wfwasmBytes(base64Rgba), width, height);
    return "ok";
};

window.wfwasm.texPatch = function (layerId, base64Rgba, x, y, width, height) {
    var layer = window.wfwasm._layers[layerId];
    if (!layer) return "missing-layer";
    var patch = new ImageData(wfwasmBytes(base64Rgba), width, height);
    var tmp = document.createElement("canvas");
    tmp.width = layer.width; tmp.height = layer.height;
    var tctx = tmp.getContext("2d");
    tctx.putImageData(layer, 0, 0);
    tctx.putImageData(patch, x, y);
    window.wfwasm._layers[layerId] = tctx.getImageData(0, 0, layer.width, layer.height);
    return "ok";
};

function wfwasmBlit(ctx, layerId, x, y, dw, dh) {
    var layer = window.wfwasm._layers[layerId];
    if (!layer) return;
    var tmp = document.createElement("canvas");
    tmp.width = layer.width; tmp.height = layer.height;
    tmp.getContext("2d").putImageData(layer, 0, 0);
    ctx.drawImage(tmp, x, y, dw, dh);
}

// Logical scene size shared with hit testing (WpfViewport contain-fit).
window.wfwasm._logical = window.wfwasm._logical || {};

window.wfwasm.configureCanvas = function (canvasId, logicalWidth, logicalHeight) {
    window.wfwasm._logical[canvasId] = { w: logicalWidth, h: logicalHeight };
    return "ok";
};

// Sizes the backing store from the displayed element size and devicePixelRatio
// (uniform contain-fit scale, aspect preserved) and returns the same
// scale/offset triple the .NET WpfViewport helper computes for input mapping.
window.wfwasm.fitCanvas = function (canvasId, logicalWidth, logicalHeight) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return null;
    var rectW = canvas.clientWidth || canvas.width;
    var rectH = canvas.clientHeight || canvas.height;
    var dpr = window.devicePixelRatio || 1;
    var scale = Math.min(rectW / logicalWidth, rectH / logicalHeight);
    if (!(scale > 0)) scale = 1;
    var backingW = Math.max(1, Math.round(logicalWidth * scale * dpr));
    var backingH = Math.max(1, Math.round(logicalHeight * scale * dpr));
    if (canvas.width !== backingW || canvas.height !== backingH) {
        canvas.width = backingW;
        canvas.height = backingH;
    }
    return [scale * dpr, (rectW - logicalWidth * scale) / 2, (rectH - logicalHeight * scale) / 2];
};

// Element-box size (CSS px) for .NET-side WpfViewport.MapToLogical. Read
// synchronously via IJSInProcessRuntime; paint calls fitCanvas every frame so
// resize/zoom/DPR changes need no extra interop on the input path.
window.wfwasm.measureCanvas = function (canvasId) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return null;
    return [canvas.clientWidth || canvas.width, canvas.clientHeight || canvas.height];
};

// Keeps moves/releases routing to the canvas when the pointer leaves it.
window.wfwasm.capturePointer = function (canvasId, pointerId) {
    var canvas = document.getElementById(canvasId);
    if (canvas && canvas.setPointerCapture) {
        try { canvas.setPointerCapture(pointerId); } catch (e) { /* already released */ }
    }
    return "ok";
};

window.wfwasm.paint = function (canvasId, commandsJson, frameSeq) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) return "missing-canvas";
    var logical = window.wfwasm._logical[canvasId] || { w: 800, h: 600 };
    window.wfwasm.fitCanvas(canvasId, logical.w, logical.h);
    var ctx = canvas.getContext("2d");
    var ops = JSON.parse(commandsJson);
    var s = canvas.width / logical.w;
    ctx.setTransform(s, 0, 0, s, 0, 0);
    ctx.save();
    ctx.clearRect(0, 0, logical.w, logical.h);
    for (var k = 0; k < ops.length; k++) {
        var op = ops[k];
        switch (op[0]) {
            case 1: ctx.fillStyle = op[5]; ctx.fillRect(op[1], op[2], op[3], op[4]); break;
            case 2:
                ctx.fillStyle = op[6];
                ctx.beginPath();
                ctx.roundRect(op[1], op[2], op[3], op[4], op[5]);
                ctx.fill();
                break;
            case 3:
                ctx.font = op[4].replace(/\bsans-serif\b/g, '"Noto Sans"').replace(/\bmonospace\b/g, '"Noto Sans Mono"'); ctx.fillStyle = op[5];
                ctx.textAlign = op[6] === 1 ? "center" : op[6] === 2 ? "right" : "left";
                ctx.textBaseline = op[7] === 1 ? "top" : op[7] === 2 ? "middle" : op[7] === 3 ? "bottom" : "alphabetic";
                ctx.fillText(op[1], op[2], op[3]);
                break;
            case 4: {
                var g = ctx.createLinearGradient(0, op[2], 0, op[2] + op[4]);
                g.addColorStop(0, op[5]); g.addColorStop(1, op[6]);
                ctx.fillStyle = g; ctx.fillRect(op[1], op[2], op[3], op[4]);
                break;
            }
            case 5:
                ctx.strokeStyle = op[6]; ctx.lineWidth = op[5]; ctx.lineCap = "round";
                ctx.beginPath(); ctx.moveTo(op[1], op[2]); ctx.lineTo(op[3], op[4]); ctx.stroke();
                break;
            case 6:
                ctx.beginPath();
                ctx.ellipse(op[1] + op[3] / 2, op[2] + op[4] / 2, op[3] / 2, op[4] / 2, 0, 0, 2 * Math.PI);
                if (op[7] !== null && op[7] !== undefined) { ctx.fillStyle = op[7]; ctx.fill(); }
                ctx.strokeStyle = op[6]; ctx.lineWidth = op[5]; ctx.stroke();
                break;
            case 7: {
                var cached = window.wfwasm._images.get(op[7]);
                if (!cached) {
                    var holder = document.createElement("canvas");
                    holder.width = op[5]; holder.height = op[6];
                    holder.getContext("2d").putImageData(new ImageData(wfwasmBytes(op[7]), op[5], op[6]), 0, 0);
                    if (window.wfwasm._images.size > 32) window.wfwasm._images.delete(window.wfwasm._images.keys().next().value);
                    window.wfwasm._images.set(op[7], holder);
                    cached = holder;
                }
                ctx.drawImage(cached, op[1], op[2], op[3], op[4]);
                break;
            }
            case 9: wfwasmBlit(ctx, op[1], op[2], op[3], op[4], op[5]); break;
            case 10:
                Object.keys(window.wfwasm._layers).forEach(function (id) {
                    if (op[1].indexOf(id) < 0) delete window.wfwasm._layers[id];
                });
                break;
            case 12: ctx.save(); ctx.beginPath(); ctx.rect(op[1], op[2], op[3], op[4]); ctx.clip(); break;
            case 13: ctx.restore(); break;
            case 16: {
                var path = new Path2D(op[1]);
                var rule = op[5] === "evenodd" ? "evenodd" : "nonzero";
                if (op[2] !== null && op[2] !== undefined) { ctx.fillStyle = op[2]; ctx.fill(path, rule); }
                if (op[3] !== null && op[3] !== undefined && op[4] > 0) { ctx.strokeStyle = op[3]; ctx.lineWidth = op[4]; ctx.stroke(path); }
                break;
            }
            case 14: ctx.save(); ctx.transform(op[1], op[2], op[3], op[4], op[5], op[6]); break;
            case 15: ctx.restore(); break;
            default: if (window.wfwasm._warned !== op[0]) { window.wfwasm._warned = op[0]; console.warn("wfwasm.paint: unsupported op", op[0]); } break;
        }
    }
    ctx.restore();
    return "ok";
};

// requestAnimationFrame pump driving WasmWpfHost.Tick/PublishAsync via OnFrame.
window.wasmWpfLoop = window.wasmWpfLoop || { running: false };
window.wasmWpfLoop.start = function (dotNet, canvasId) {
    window.wasmWpfLoop.stop();
    window.wasmWpfLoop.running = true;
    window.wasmWpfLoop.canvasId = canvasId;
    var generation = window.wasmWpfLoop.generation;
    var tick = async function (ts) {
        if (!window.wasmWpfLoop.running || generation !== window.wasmWpfLoop.generation) return;
        try { await dotNet.invokeMethodAsync("OnFrame", ts); }
        catch (error) { window.wasmWpfLoop.stop(); window.studioRuntime?.fail(error.message); return; }
        if (window.wasmWpfLoop.running && generation === window.wasmWpfLoop.generation)
            requestAnimationFrame(tick);
    };
    window.wasmWpfLoop.resize = new ResizeObserver(() => dotNet.invokeMethodAsync("OnResize").catch(error => window.studioRuntime?.fail(error.message)));
    window.wasmWpfLoop.resize.observe(document.getElementById(canvasId));
    requestAnimationFrame(tick);
    return "ok";
};
window.wasmWpfLoop.stop = function () {
    window.wasmWpfLoop.running = false;
    window.wasmWpfLoop.generation = (window.wasmWpfLoop.generation || 0) + 1;
    window.wasmWpfLoop.resize?.disconnect();
    return "ok";
};
