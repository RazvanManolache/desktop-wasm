// Backend controls operate on the current app and its current visual tree.
window.wasmWpfRenderer = (() => {
    let dotnet = null, canvas = null, onLost = null;
    const api = {
        initialize(reference) {
            dotnet = reference;
            const query = new URLSearchParams(location.search);
            const preference = query.get('renderer') || (location.pathname.startsWith('/demo/') ? 'canvas2d' : 'auto');
            const probe = document.createElement('canvas');
            const gl = probe.getContext('webgl2');
            let device = '';
            if (gl) {
                const info = gl.getExtension('WEBGL_debug_renderer_info');
                device = info ? String(gl.getParameter(info.UNMASKED_RENDERER_WEBGL)) : String(gl.getParameter(gl.RENDERER));
                gl.getExtension('WEBGL_lose_context')?.loseContext();
            }
            return {present: !!gl, device, preference: ['auto', 'webgl', 'canvas2d'].includes(preference) ? preference : 'auto', diagnostics: query.get('diagnostics') === '1'};
        },
        async loadFonts() { await Promise.all([document.fonts.load('14px "Noto Sans"'), document.fonts.load('14px "Noto Sans Mono"')]); },
        watchContext(id) {
            api.unwatch();
            canvas = document.getElementById(id);
            if (!canvas) return false;
            onLost = event => { event.preventDefault(); dotnet?.invokeMethodAsync('OnGpuContextLost').catch(error => window.studioRuntime.fail(error.message)); };
            canvas.addEventListener('webglcontextlost', onLost);
            return true;
        },
        unwatch() { if (canvas && onLost) canvas.removeEventListener('webglcontextlost', onLost); canvas = null; onLost = null; },
        async setRenderer(value) { if (dotnet) await dotnet.invokeMethodAsync('SetRenderer', value); },
        async setDiagnostics(enabled) { if (dotnet) await dotnet.invokeMethodAsync('SetDiagnostics', enabled); },
        telemetry(json) {
            const node = document.getElementById('wpfTelemetry');
            if (node) node.textContent = String(json);
        },
        async capture() { if (dotnet) await dotnet.invokeMethodAsync('CaptureGpu'); },
        loseContext() {
            const gl = document.getElementById('wpfCanvas')?.getContext('webgl2');
            const extension = gl?.getExtension('WEBGL_lose_context');
            if (!extension) return false;
            extension.loseContext(); return true;
        },
        capturePointer(id, pointerId) {
            const target = document.getElementById(id);
            target?.focus({preventScroll: true});
            try { target?.setPointerCapture(pointerId); } catch { /* released pointer */ }
        },
        dispose() { api.unwatch(); dotnet = null; }
    };
    return api;
})();
