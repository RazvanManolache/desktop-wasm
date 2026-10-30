import {resourceTotals} from './studio-core.mjs';

const session = new URLSearchParams(location.search).get('session');
const slug = decodeURIComponent(location.pathname.split('/').pop());
let failed = false, ready = false, backend = 'Canvas2D';
function send(type, payload = {}) {
    if (window.parent !== window && session)
        window.parent.postMessage({channel: 'wpf-studio', session, slug, type, ...payload}, location.origin);
}
function metrics() { send('metrics', resourceTotals(performance.getEntriesByType('resource'))); }
window.studioRuntime = {
    ready() { if (ready || failed) return; ready = true; send('ready', {runtimeMs: performance.now()}); metrics(); },
    progress(phase) { send('progress', {phase}); },
    renderer(info) { backend = info.backend; send('renderer', info); },
    screenshot(png) { send('screenshot', {png}); },
    fail(message) { if (failed) return; failed = true; send('error', {message: String(message), phase: ready ? 'interaction' : 'loading'}); metrics(); }
};
addEventListener('error', event => window.studioRuntime.fail(event.message || 'Runtime script failed'));
addEventListener('unhandledrejection', event => window.studioRuntime.fail(event.reason?.message || event.reason || 'Runtime promise rejected'));
new PerformanceObserver(metrics).observe({type: 'resource', buffered: true});
// This is a liveness signal, not an FPS counter or a performance claim.
setInterval(() => send('heartbeat'), 2000);
addEventListener('message', event => {
    const d = event.data;
    if (event.source !== parent || event.origin !== location.origin || d?.channel !== 'wpf-studio' || d.session !== session) return;
    if (d.type === 'capture') {
        try {
            if (backend.startsWith('WebGL')) { window.wasmWpfRenderer.capture().catch(error => send('error', {message: error.message, phase: 'screenshot'})); return; }
            const canvas = document.getElementById('wpfCanvas');
            if (!ready || !canvas) throw new Error('No painted application canvas to capture');
            send('screenshot', {png: canvas.toDataURL('image/png')});
        } catch (error) { send('error', {message: error.message, phase: 'screenshot'}); }
    }
    if (d.type === 'set-renderer') window.wasmWpfRenderer.setRenderer(d.preference).catch(error => window.studioRuntime.fail(error.message));
    if (d.type === 'set-diagnostics') window.wasmWpfRenderer.setDiagnostics(d.enabled).catch(error => window.studioRuntime.fail(error.message));
    if (d.type === 'lose-context') window.wasmWpfRenderer.loseContext();
});
send('progress', {phase: 'Downloading the .NET runtime…'});
