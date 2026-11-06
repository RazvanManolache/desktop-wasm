import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { AppSessions, sessionUrl } from './shell-core.mjs';

// FACTORY-shell-4: the address bar must name the RUNNING session. The server
// mints its own id into mount redirects and history restores dead ids; the
// shell canonicalizes to its live session id on every select (replaceState).
function stubSessions() {
    let n = 0;
    return new AppSessions({
        runtimeOrigin: () => 'http://localhost:9999',
        parentOrigin: 'http://127.0.0.1:5220',
        mount: () => ({}),
        unmount: () => {},
        changed: () => {},
        now: () => 1000,
        id: () => `live-${++n}`,
    });
}

describe('sessionUrl', () => {
    it('builds the canonical running-session URL', () => {
        assert.equal(sessionUrl({ slug: 'paint' }, 'abc123'), '/app/paint?session=abc123');
    });
    it('encodes slug and session id', () => {
        assert.equal(sessionUrl({ slug: 'a b' }, 'x/y'), '/app/a%20b?session=x%2Fy');
    });
});

describe('AppSessions.select', () => {
    it('returns the live session with a fresh id per select', () => {
        const sessions = stubSessions();
        const a = sessions.select({ slug: 'smoke', label: 'Smoke' });
        const b = sessions.select({ slug: 'paint', label: 'Paint' });
        assert.ok(a.id && b.id && a.id !== b.id);
        assert.equal(sessions.current, b);
    });
    it('canonical URL tracks the current session after reselect', () => {
        const sessions = stubSessions();
        sessions.select({ slug: 'smoke', label: 'Smoke' });
        const live = sessions.select({ slug: 'smoke', label: 'Smoke' });
        assert.equal(sessionUrl(live.app, live.id), `/app/smoke?session=${live.id}`);
    });
});
