import {searchApps, readRoute, acceptsMessage, Sessions, validateCatalog} from './studio-core.mjs';

const $ = id => document.getElementById(id);
const sessions = new Sessions();
const mountRoot = new URL(document.baseURI).pathname.replace(/\/$/, '');
const errors = [];
let apps = [], selected = null, frame = null, timer = null, clock = null, started = 0, heartbeat = 0;
let query = '', filter = 'all', category = 'all';
let rendererChoice = 'auto', diagnostics = false;
try { rendererChoice = localStorage.getItem('wpf-renderer') || 'auto'; diagnostics = localStorage.getItem('wpf-renderer-diagnostics') === '1'; } catch { /* storage optional */ }
if (!['auto', 'webgl', 'canvas2d'].includes(rendererChoice)) rendererChoice = 'auto';
$('renderer-choice').value = rendererChoice; $('renderer-diagnostics').checked = diagnostics;
$('renderer-timings').hidden = !diagnostics;
const node = (tag, text, cls) => { const el = document.createElement(tag); if (text) el.textContent = text; if (cls) el.className = cls; return el; };
const ms = value => value < 1000 ? `${Math.round(value)} ms` : `${(value / 1000).toFixed(2)} s`;
const mb = value => `${(value / 1048576).toFixed(2)} MB`;

function status(text, state) { $('status').textContent = text; $('status').dataset.state = state; }
function controls(active, capture = false) {
    $('restart').disabled = !selected;
    $('stop').disabled = !active;
    $('capture').disabled = !capture;
}
function tearDown() {
    clearTimeout(timer); clearInterval(clock); timer = clock = null;
    sessions.stop();
    if (frame) { frame.remove(); frame = null; }
    $('renderer-backend').textContent = 'No app'; $('renderer-device').textContent = 'Select an app to see its actual backend.';
    $('renderer-reason').textContent = ''; $('renderer-loss').disabled = true;
}
function navigate(slug) {
    const url = mountRoot + (slug ? `/app/${encodeURIComponent(slug)}` : '/');
    if (location.pathname !== url) history.pushState(null, '', url);
    openRoute();
}
function select(app) {
    tearDown(); selected = app; $('stage').replaceChildren();
    $('app-title').textContent = app.label;
    document.title = `${app.label} · WPF Studio`;
    $('app-kind').textContent = app.kind === 'external' ? 'EXTERNAL WPF APPLICATION' : 'ENGINE-NATIVE TOOLKIT DEMO';
    $('renderer-backend').textContent = 'Starting…'; $('renderer-reason').textContent = '';
    $('renderer-draw').textContent = $('renderer-input').textContent = '—'; $('renderer-loss').disabled = true;
    $('stage-caption').textContent = app.description;
    $('source').hidden = !app.source; if (app.source) $('source').href = app.source;
    for (const id of ['load-time', 'loaded-mb', 'network-mb', 'cache-count', 'runtime-time']) $(id).textContent = '—';
    renderLibrary();
    document.querySelector('.studio-shell').classList.remove('library-open');
    start(app);
}
function start(app) {
    tearDown(); $('stage').replaceChildren();
    $('renderer-backend').textContent = 'Starting…';
    const token = crypto.randomUUID();
    sessions.start(app.slug, token); started = performance.now(); heartbeat = started;
    status('Opening…', 'loading'); controls(true);
    frame = document.createElement('iframe');
    frame.title = `${app.label} — isolated WPF runtime`;
    frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-downloads');
    // Frame boundary provides fault/state isolation, not a security sandbox for hostile code.
    frame.src = `${mountRoot}/runtime/${app.slug}?session=${encodeURIComponent(token)}&renderer=${encodeURIComponent(rendererChoice)}&diagnostics=${diagnostics ? '1' : '0'}`;
    $('stage').append(frame);
    overlay(`Opening ${app.label}`, 'Starting a fresh runtime. You can keep browsing the library while this app loads.', null);
    clock = setInterval(() => {
        if (sessions.current?.status === 'loading') $('load-time').textContent = ms(performance.now() - started);
        if (sessions.current?.status === 'ready' && performance.now() - heartbeat > 12000)
            $('foot-status').textContent = 'The app is not responding. Restart it, stop it, or choose another app.';
    }, 500);
    armStartupTimer();
    $('foot-status').textContent = 'A new app runtime is loading. The library remains available.';
}
function armStartupTimer() {
    clearTimeout(timer); timer = null;
    // Tiered startup budget (verify-2 decision): 60s base, extended ONLY by
    // progress/renderer evidence while loading — never by heartbeat/metrics —
    // hard-capped by the per-app manifest budget (loadBudgetMs, default 600s).
    const cap = selected?.loadBudgetMs ?? 600000;
    const remaining = Math.min(60000, started + cap - performance.now());
    if (remaining <= 0) { fail('The app did not paint a frame within its startup budget.', 'startup-timeout'); return; }
    timer = setTimeout(() => fail('The app did not paint a frame within 60 seconds.', 'startup-timeout'), remaining);
}
function overlay(title, description, icon, isError = false, actions = []) {
    $('stage').querySelector('.studio-overlay')?.remove();
    const panel = node('div', null, `studio-overlay${isError ? ' error' : ''}`);
    panel.setAttribute('role', isError ? 'alert' : 'status');
    panel.append(node('span', icon, icon ? 'overlay-icon' : 'spinner'), node('h2', title), node('p', description));
    if (actions.length) {
        const buttons = node('div', null, 'studio-actions');
        for (const [label, action] of actions) { const b = node('button', label); b.onclick = action; buttons.append(b); }
        panel.append(buttons);
    }
    $('stage').append(panel);
}
function report(message, phase) {
    errors.unshift({app: selected?.label || 'Launcher', slug: selected?.slug || null, session: sessions.current?.token || null,
        phase, message: String(message), time: new Date().toISOString()});
    errors.splice(50); $('error-count').textContent = errors.length;
}
function fail(message, phase) {
    if (sessions.current?.status === 'failed') return;
    report(message, phase);
    const token = sessions.current?.token;
    sessions.update(token, 'failed');
    clearTimeout(timer); clearInterval(clock); frame?.remove(); frame = null;
    status('Failed to open', 'failed'); controls(false);
    overlay(`${selected?.label || 'App'} could not continue`, message, '!', true,
        [['Try again', () => selected && start(selected)], ['View error report', showErrors], ['Back to workspace', () => navigate(null)]]);
    $('foot-status').textContent = 'This app session ended. Choose another app or restart.';
}
function stop() {
    tearDown(); status('Stopped', 'stopped'); controls(false);
    overlay(`${selected.label} is stopped`, 'The app runtime has been released. Your library is ready for another app.', '□', false,
        [['Open again', () => start(selected)], ['Back to workspace', () => navigate(null)]]);
    $('foot-status').textContent = 'Session stopped · no app runtime running.';
}
function renderLibrary() {
    const entries = searchApps(apps, {query, filter, category});
    $('apps').replaceChildren(); $('library-count').textContent = `${entries.length} / ${apps.length}`;
    for (const group of [...new Set(entries.map(a => a.category))]) {
        const members = entries.filter(a => a.category === group);
        const heading = node('div', group, 'studio-group'); heading.append(node('span', `${members.length}`)); $('apps').append(heading);
        for (const app of members) {
            const b = node('button', null, `studio-app ${app.status} ${app.kind}`);
            if (app.slug === selected?.slug) b.setAttribute('aria-current', 'page');
            b.dataset.slug = app.slug;
            b.title = `${app.description} · ${app.status === 'ready' ? 'Verified workflow' : 'Run original application'}`;
            const copy = node('span', null, 'studio-app-copy');
            copy.append(node('strong', app.label), node('small', app.description));
            b.append(node('span', app.icon, 'studio-app-icon'), copy, node('span', null, 'studio-app-state'));
            b.onclick = () => navigate(app.slug); $('apps').append(b);
        }
    }
    if (!entries.length) {
        const empty = node('div', 'No apps match your search.', 'studio-empty');
        const reset = node('button', 'Reset filters'); reset.onclick = () => {
            query = ''; filter = 'all'; category = 'all'; $('search').value = ''; $('category').value = 'all'; syncFilters(); renderLibrary();
        }; empty.append(reset); $('apps').append(empty);
    }
}
function home() {
    tearDown(); selected = null; renderLibrary(); controls(false); status('Choose an app', 'idle');
    $('app-title').textContent = 'Desktop apps, a new home.'; document.title = 'WPF Studio · Desktop apps, a new home';
    $('app-kind').textContent = 'YOUR WPF WORKSPACE'; $('source').hidden = true;
    $('stage-caption').textContent = 'Your application workspace';
    for (const id of ['load-time', 'loaded-mb', 'network-mb', 'cache-count', 'runtime-time']) $(id).textContent = '—';
    const welcome = node('div', null, 'studio-welcome');
    welcome.append(node('div', 'PRESERVE THE KNOWLEDGE. EXPLORE THE POSSIBILITIES.', 'welcome-kicker'),
        node('h2', 'Familiar apps.\nA fresh place to run.'),
        node('p', 'Explore desktop interfaces in your browser. Start with a working sample, try the toolkit, or inspect the next compatibility targets. Each app opens in its own recoverable session.'));
    const cards = node('div', null, 'welcome-cards');
    for (const slug of ['hello-real', 'pocket-calculator', 'project-planner']) {
        const app = apps.find(a => a.slug === slug); if (!app) continue;
        const card = node('button', null, 'welcome-card'); card.onclick = () => navigate(slug);
        card.append(node('span', app.icon, 'studio-app-icon'), node('strong', app.label + ' ↗'), node('small', app.description)); cards.append(card);
    }
    welcome.append(cards);
    const note = node('div', null, 'welcome-note');
    note.append(node('b', `${apps.filter(a => a.status === 'ready').length} ready`),
        node('span', 'Toolkit scenes exercise the engine. External targets retain their source and compatibility status. Use Rendering to choose WebGL or Canvas2D for the actual open application. WPF compatibility remains in progress.'));
    welcome.append(note); $('stage').replaceChildren(welcome);
    $('foot-status').textContent = 'The library is ready. No app runtime loaded.';
}
function openRoute() {
    const route = readRoute(location.pathname, mountRoot);
    if (!route) { home(); return; }
    const app = route.slug && apps.find(a => a.slug === route.slug);
    if (app) { select(app); return; }
    home(); status('Unknown app', 'blocked');
    overlay('This app is not in the library', 'Choose an app from the library or return to the workspace.', '?', false, [['Back to workspace', () => navigate(null)]]);
}
async function showDetails() {
    const content = $('info-content'); content.replaceChildren();
    if (!selected) {
        content.append(node('h2', 'A new home for desktop knowledge'), node('p', 'This preview explores preserving desktop business logic while adapting the platform boundary. Toolkit demos use engine types; the converted HelloWorld sample uses original app IL remapped through the compatibility layer. Larger WPF apps still need compatibility work.'));
    } else {
        content.append(node('div', selected.kind === 'external' ? 'EXTERNAL WPF TARGET' : 'ENGINE-NATIVE DEMO', 'studio-eyebrow'), node('h2', selected.label), node('p', selected.description));
        const dl = node('dl');
        for (const [key, value] of Object.entries({Status: selected.status === 'ready' ? 'Verified workflow' : 'Original app — actual conversion result is loaded when opened',
            Scope: selected.boundary || 'Engine-native demonstration, not a converted third-party application.', License: selected.license || 'Project license not yet selected', Project: selected.project || 'DesktopWasm.BrowserHost', 'Source commit': selected.commit || 'Local project'})) {
            dl.append(node('dt', key), node('dd', value));
        }
        content.append(dl);
        if (selected.source) { const link = node('a', 'Open upstream source ↗'); link.href = selected.source; link.target = '_blank'; link.rel = 'noopener noreferrer'; content.append(link); }
        if (selected.recipe) { content.append(node('p', 'Rebuild and convert the original app locally:'), node('code', `pwsh scripts/Build-WpfApps.ps1 -Id ${selected.slug}`)); }
        if (selected.kind === 'external') {
            const slug = selected.slug;
            try {
                const response = await fetch(`apps/${slug}/app-manifest.json`, {cache: 'no-cache'});
                if (!response.ok) throw new Error(`Conversion report request failed (${response.status})`);
                const result = await response.json();
                if (selected?.slug !== slug) return;
                content.append(node('h3', 'Actual conversion result'), node('p', `${result.status} · ${result.assemblies.length} referenced assemblies · ${result.gaps.length} API/dependency gaps`));
                if (result.gaps.length) content.append(node('pre', result.gaps.slice(0, 12).map(g => `${g.assembly} [${g.kind}] ${g.detail}`).join('\n'), 'conversion-gaps'));
                const save = node('button', 'Download full conversion report');
                save.onclick = () => download(new Blob([JSON.stringify(result, null, 2)], {type:'application/json'}), `${slug}-conversion.json`);
                content.append(save);
            } catch (error) { content.append(node('p', error.message)); }
        }
    }
    $('info-dialog').showModal();
}
function showErrors() {
    $('error-list').replaceChildren();
    if (!errors.length) $('error-list').append(node('p', 'No session errors have been reported.'));
    for (const error of errors) {
        const card = node('div', null, 'studio-error-entry');
        card.append(node('strong', `${error.app} · ${error.phase}`), node('small', `${error.time} · session ${error.session || 'launcher'}`), node('pre', error.message)); $('error-list').append(card);
    }
    $('error-dialog').showModal();
}
function download(blob, name) {
    const link = document.createElement('a'); const url = URL.createObjectURL(blob); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function syncFilters() { document.querySelectorAll('[data-filter]').forEach(b => b.setAttribute('aria-pressed', b.dataset.filter === filter)); }
$('search').oninput = e => { query = e.target.value; renderLibrary(); };
$('category').onchange = e => { category = e.target.value; renderLibrary(); };
document.querySelectorAll('[data-filter]').forEach(b => b.onclick = () => { filter = b.dataset.filter; syncFilters(); renderLibrary(); });
$('menu').onclick = () => {
    const shell = document.querySelector('.studio-shell');
    const narrow = matchMedia('(max-width: 760px)').matches;
    shell.classList.remove(narrow ? 'library-hidden' : 'library-open');
    shell.classList.toggle(narrow ? 'library-open' : 'library-hidden');
    $('menu').setAttribute('aria-expanded', narrow ? shell.classList.contains('library-open') : !shell.classList.contains('library-hidden'));
};
$('restart').onclick = () => selected && start(selected); $('stop').onclick = stop;
function runtimeCommand(type, payload = {}) { frame?.contentWindow.postMessage({channel: 'wpf-studio', session: sessions.current?.token, type, ...payload}, location.origin); }
$('rendering').onclick = () => { $('rendering-panel').hidden = !$('rendering-panel').hidden; $('rendering').setAttribute('aria-expanded', !$('rendering-panel').hidden); };
$('renderer-choice').onchange = event => {
    rendererChoice = event.target.value;
    try { localStorage.setItem('wpf-renderer', rendererChoice); } catch { /* storage optional */ }
    runtimeCommand('set-renderer', {preference: rendererChoice});
};
$('renderer-diagnostics').onchange = event => {
    diagnostics = event.target.checked; $('renderer-timings').hidden = !diagnostics;
    try { localStorage.setItem('wpf-renderer-diagnostics', diagnostics ? '1' : '0'); } catch { /* storage optional */ }
    runtimeCommand('set-diagnostics', {enabled: diagnostics});
};
$('renderer-loss').onclick = () => runtimeCommand('lose-context');
$('details').onclick = showDetails; $('errors').onclick = showErrors;
$('export-errors').onclick = () => download(new Blob([JSON.stringify({version: 1, errors}, null, 2)], {type: 'application/json'}), 'wpf-studio-errors.json');
$('capture').onclick = () => frame?.contentWindow.postMessage({channel: 'wpf-studio', session: sessions.current?.token, type: 'capture'}, location.origin);
document.querySelector('.studio-brand').onclick = e => { e.preventDefault(); navigate(null); };
addEventListener('popstate', openRoute);
addEventListener('message', event => {
    const current = sessions.current;
    if (!current || !frame || !acceptsMessage(event, {origin: location.origin, source: frame.contentWindow, session: current.token, slug: current.slug})) return;
    const d = event.data;
    if (d.type === 'renderer') {
        if (!['WebGL · Skia', 'Canvas2D'].includes(d.backend)) return;
        $('renderer-backend').textContent = d.backend;
        $('renderer-device').textContent = d.device || (d.available ? 'WebGL available' : 'WebGL unavailable');
        $('renderer-reason').textContent = d.fallback || '';
        $('renderer-loss').disabled = !d.backend.startsWith('WebGL');
        $('renderer-draw').textContent = Number.isFinite(d.drawMs) ? ms(d.drawMs) : '—';
        $('renderer-input').textContent = Number.isFinite(d.inputMs) ? ms(d.inputMs) : '—';
        if (current.status === 'loading') armStartupTimer();
        return;
    }
    if (d.type === 'heartbeat') { heartbeat = performance.now(); return; }
    if (d.type === 'metrics') {
        if (![d.loadedBytes, d.networkBytes, d.reusedResponses, d.requests].every(v => Number.isFinite(v) && v >= 0)) return;
        $('loaded-mb').textContent = mb(d.loadedBytes); $('network-mb').textContent = mb(d.networkBytes);
        $('cache-count').textContent = `${d.reusedResponses} / ${d.requests}`; return;
    }
    if (d.type === 'progress' && current.status === 'loading') { $('foot-status').textContent = `${selected.label} · ${String(d.phase).slice(0, 200)}`; armStartupTimer(); return; }
    if (d.type === 'ready' && current.status === 'loading') {
        sessions.update(current.token, 'ready'); clearTimeout(timer); heartbeat = performance.now();
        $('stage').querySelector('.studio-overlay')?.remove(); status('Running', 'ready'); controls(true, true);
        $('load-time').textContent = ms(performance.now() - started);
        $('runtime-time').textContent = Number.isFinite(d.runtimeMs) ? ms(d.runtimeMs) : '—';
        $('foot-status').textContent = `${selected.label} · first frame painted · isolated app session`; return;
    }
    if (d.type === 'error') {
        if (d.phase === 'screenshot') { report(d.message, d.phase); showErrors(); }
        else fail(String(d.message).slice(0, 8000), d.phase || 'runtime');
    }
    if (d.type === 'screenshot' && current.status === 'ready' && /^data:image\/png;base64,/.test(d.png)) {
        const bytes = Uint8Array.from(atob(d.png.split(',')[1]), c => c.charCodeAt(0));
        download(new Blob([bytes], {type: 'image/png'}), `${selected.slug}.png`);
    }
});
try {
    const response = await fetch('app-library.json');
    if (!response.ok) throw new Error(`Library request failed (${response.status})`);
    apps = validateCatalog(await response.json());
    for (const name of [...new Set(apps.map(a => a.category))]) { const option = node('option', name); option.value = name; $('category').append(option); }
    openRoute();
    if (new URLSearchParams(location.search).get('rendering') === '1') { $('rendering-panel').hidden = false; $('rendering').setAttribute('aria-expanded', 'true'); }
} catch (error) {
    report(error.message, 'catalog'); status('Library unavailable', 'failed');
    overlay('The app library could not load', error.message, '!', true, [['Reload library', () => location.reload()]]);
}
