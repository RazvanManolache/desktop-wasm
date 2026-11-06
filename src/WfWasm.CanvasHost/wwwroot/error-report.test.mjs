// JS error-report identity: every window.onerror / unhandledrejection /
// runtime-console row must name the serving pair, matching the in-WASM
// BrowserErrorReporter backend/generation fields. Backend is the page's own
// origin host (same shape as the managed HttpClient-authority fallback);
// generation has no client-side source yet and travels only when the shell
// stamps window.wfwasmRuntime.generation (unset means unknown).
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const src = fs.readFileSync(path.join(here, 'canvas-bridge.js'), 'utf8');

function loadBridge({ host, runtime } = {}) {
    const posted = [];
    const listeners = {};
    const sandbox = {
        location: { host: host ?? '127.0.0.1:5220', pathname: '/app/paint' },
        fetch: (url, opts) => {
            posted.push({ url, body: JSON.parse(opts.body) });
            return Promise.resolve({ ok: true });
        },
        setTimeout: () => 0,
        clearTimeout: () => {},
        performance: { now: () => 0 },
        URL,
        console: {
            log: () => {}, info: () => {}, warn: () => {},
            debug: () => {}, error: () => {},
        },
        window: {
            addEventListener: (type, fn) => { listeners[type] = fn; },
        },
    };
    sandbox.window.window = sandbox.window;
    sandbox.globalThis = sandbox;
    if (runtime !== undefined) sandbox.window.wfwasmRuntime = runtime;
    vm.createContext(sandbox);
    vm.runInContext(src, sandbox, { filename: 'canvas-bridge.js' });
    sandbox.window.wfwasm.installErrorReporting();
    return { sandbox, posted, listeners };
}

describe('error-report identity', () => {
    it('names the serving backend authority on fatal rows', () => {
        const { posted, listeners } = loadBridge({
            host: '127.0.0.1:5220',
            runtime: { app: 'paint', label: 'Paint', session: 's1', fail: () => {} },
        });
        listeners['error']({ error: { name: 'Error', message: 'boom-backend-marker', stack: 'Error: x' } });
        assert.equal(posted.length, 1);
        const row = posted[0].body[0];
        assert.equal(row.message, 'boom-backend-marker');
        assert.equal(row.backend, '127.0.0.1:5220');
    });

    it('carries a stamped generation when the shell provides one', () => {
        const { posted, listeners } = loadBridge({
            host: '127.0.0.1:5230',
            runtime: { app: 'paint', session: 's2', fail: () => {}, generation: 'gen-stamp-marker' },
        });
        listeners['error']({ error: { name: 'Error', message: 'boom-gen-marker', stack: 'Error: y' } });
        assert.equal(posted.length, 1);
        const row = posted[0].body[0];
        assert.equal(row.backend, '127.0.0.1:5230');
        assert.equal(row.generation, 'gen-stamp-marker');
    });

    it('leaves generation null when unstamped, keeps existing fields', () => {
        const { posted, listeners } = loadBridge({
            runtime: { app: 'paint', session: 's3', fail: () => {} },
        });
        listeners['error']({ error: { name: 'Error', message: 'boom-plain-marker', stack: null } });
        const row = posted[0].body[0];
        assert.equal(row.generation, null);
        assert.equal(row.app, 'paint');
        assert.equal(row.sessionId, 's3');
        assert.match(row.signature, /^Error\|/);
    });
});
