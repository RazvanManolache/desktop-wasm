import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import {createHash} from 'node:crypto';
import {searchApps, readRoute, resourceTotals, acceptsMessage, Sessions, validateCatalog} from '../../src/DesktopWasm.BrowserHost/wwwroot/studio-core.mjs';

const root = new URL('../../', import.meta.url);
const load = path => readFile(new URL(path, root), 'utf8');
const apps = JSON.parse(await load('src/DesktopWasm.BrowserHost/wwwroot/app-library.json'));

test('GPU rendering is a workspace control, never a separate app or synthetic scene', async () => {
    assert.ok(!apps.some(app => app.slug === 'gpu-spike' || app.kind === 'diagnostic'));
    const html = await load('src/DesktopWasm.BrowserHost/wwwroot/index.html');
    assert.match(html, /id="renderer-choice"/);
    assert.match(html, /id="renderer-diagnostics" type="checkbox"\s*\//);
    assert.doesNotMatch(html, /id="renderer-diagnostics"[^>]*checked/);
    const runtime = await load('src/DesktopWasm.BrowserHost/Pages/Home.razor');
    assert.match(runtime, /Replay\(canvas, _host.CurrentCommands\)/);
    assert.doesNotMatch(runtime, /PaintScene/);
    const entry = await load('src/DesktopWasm.BrowserHost/wwwroot/entry.mjs');
    assert.doesNotMatch(entry, /script\('js\/gpu-spike.js'\)/);
});

test('catalog preserves all original demos and adds playable mini apps and pinned external targets', async () => {
    assert.equal(validateCatalog(apps), apps);
    const source = await load('src/DesktopWasm.BrowserHost/Demos.cs');
    const runtimeSlugs = [...source.matchAll(/new\("([a-z0-9-]+)",/g)].map(m => m[1]);
    const ready = apps.filter(a => a.status === 'ready' && a.kind !== 'diagnostic').map(a => a.slug);
    for (const slug of runtimeSlugs) assert.ok(ready.includes(slug), `Static runtime entry missing from ready library: ${slug}`);
    for (const app of apps.filter(a => a.status === 'ready' && a.kind === 'external')) {
        const base = `src/DesktopWasm.BrowserHost/wwwroot/apps/${app.slug}/`;
        const manifest = JSON.parse(await load(base + 'app-manifest.json'));
        assert.equal(manifest.status, 'converted'); assert.equal(manifest.slug, app.slug);
        assert.ok(manifest.assemblies.some(a => a.name === manifest.entryAssembly));
        assert.deepEqual(manifest.gaps, []);
        for (const assembly of manifest.assemblies) {
            const image = await readFile(new URL(base + assembly.file, root));
            assert.equal(createHash('sha256').update(image).digest('hex'), assembly.sha256);
        }
    }
    for (const slug of ['hello-xaml', 'controls', 'typography', 'layout', 'input', 'interop',
        'calc-keypad', 'binding', 'scroll', 'menu', 'dialogs', 'listbox', 'treeview', 'datagrid', 'resources', 'hello-real'])
        assert.ok(ready.includes(slug), `Original app removed: ${slug}`);
    assert.ok(ready.length >= 20);
    assert.ok(apps.filter(a => a.kind === 'external').length >= 14);
    for (const app of apps.filter(a => a.kind === 'external')) {
        assert.match(app.commit, /^[a-f0-9]{40}$/);
        assert.ok(app.project.endsWith('.csproj'));
        assert.ok(app.boundary);
    }
});

test('every external app has an attempted package and no static preparation gate', async () => {
    const runtime = await load('src/DesktopWasm.BrowserHost/Pages/Home.razor');
    assert.match(runtime, /ApplicationPackageLoader.CreateAsync/);
    assert.doesNotMatch(runtime, /s_onDemandApps/);
    const shell = await load('src/DesktopWasm.BrowserHost/wwwroot/studio.mjs');
    assert.doesNotMatch(shell, /if \(app.status !== 'ready'\)/);
    assert.doesNotMatch(shell, /Source target · not converted|View preparation details/);
    for (const app of apps.filter(a => a.kind === 'external')) {
        const manifest = JSON.parse(await load(`src/DesktopWasm.BrowserHost/wwwroot/apps/${app.slug}/app-manifest.json`));
        assert.equal(manifest.slug, app.slug);
        assert.ok(['converted', 'conversion-failed', 'build-failed'].includes(manifest.status));
        if (manifest.status !== 'converted') assert.ok(manifest.gaps.length > 0);
    }
});
test('catalog rejects duplicate and malformed slugs, missing source and optimistic blocked states', () => {
    assert.throws(() => validateCatalog([apps[0], apps[0]]));
    assert.throws(() => validateCatalog([{...apps[0], slug: '../escape'}]));
    assert.throws(() => validateCatalog([{...apps[0], kind: 'external', source: 'javascript:alert(1)'}]));
    assert.throws(() => validateCatalog([{...apps[0], status: 'not-converted', boundary: ''}]));
});
test('search combines words, category and readiness without hiding the original library', () => {
    assert.equal(searchApps(apps).length, apps.length);
    assert.deepEqual(searchApps(apps, {query: 'CALCULATOR memory'}).map(a => a.slug), ['pocket-calculator']);
    assert.ok(searchApps(apps, {filter: 'external'}).every(a => a.kind === 'external'));
    assert.ok(searchApps(apps, {filter: 'ready'}).every(a => a.status === 'ready'));
    assert.equal(searchApps(apps, {query: 'does-not-exist'}).length, 0);
    assert.ok(searchApps(apps, {category: 'Mini apps', filter: 'toolkit'}).length >= 4);
});
test('routes handle home, deep links, unknown routes and invalid escaping', () => {
    assert.equal(readRoute('/'), null);
    assert.deepEqual(readRoute('/app/hello-real'), {slug: 'hello-real'});
    assert.deepEqual(readRoute('/app/hello-real/'), {slug: 'hello-real'});
    assert.ok(readRoute('/app/%xx').unknown);
    assert.ok(readRoute('/not-an-app').unknown);
});
test('metrics separate decoded bytes, network overhead and reused HTTP responses', () => {
    assert.deepEqual(resourceTotals([
        {decodedBodySize: 4000, encodedBodySize: 1000, transferSize: 1300},
        {decodedBodySize: 8000, encodedBodySize: 2000, transferSize: 0},
        {decodedBodySize: 0, encodedBodySize: 0, transferSize: 0},
    ]), {loadedBytes: 12000, networkBytes: 1300, reusedResponses: 1, requests: 3});
    assert.deepEqual(resourceTotals([]), {loadedBytes: 0, networkBytes: 0, reusedResponses: 0, requests: 0});
});
test('messages require exact origin, frame window, session and application', () => {
    const source = {}; const context = {origin: 'http://localhost:5199', source, session: 'current', slug: 'input'};
    const event = {origin: context.origin, source, data: {channel: 'wpf-studio', session: 'current', slug: 'input', type: 'ready'}};
    assert.ok(acceptsMessage(event, context));
    for (const mutated of [
        {...event, origin: 'https://other.test'}, {...event, source: {}},
        {...event, data: {...event.data, session: 'old'}}, {...event, data: {...event.data, slug: 'other'}},
        {...event, data: {...event.data, channel: 'other'}}, {...event, data: {...event.data, type: 'arbitrary'}},
    ]) assert.equal(acceptsMessage(mutated, context), false);
    assert.equal(acceptsMessage(event, {...context, source: null}), false);
});
test('failed and stopped sessions cannot become ready; a new app always has its own generation', () => {
    const sessions = new Sessions();
    const old = sessions.start('bad', 'a');
    assert.ok(sessions.update('a', 'failed'));
    assert.equal(sessions.update('a', 'ready'), false);
    const current = sessions.start('input', 'b');
    assert.ok(current.generation > old.generation);
    assert.equal(sessions.update('a', 'failed'), false);
    assert.ok(sessions.update('b', 'ready'));
    sessions.stop(); assert.equal(sessions.update('b', 'ready'), false);
});
test('runtime loop never overlaps .NET frame calls and stop prevents old loops restarting', async () => {
    const callbacks = []; let pending, count = 0;
    const scope = {window: {wfwasm: {}}, document: {getElementById: () => ({})}, requestAnimationFrame: cb => callbacks.push(cb),
        ResizeObserver: class { observe() {} disconnect() {} }};
    vm.runInNewContext(await load('src/DesktopWasm.BrowserHost/wwwroot/wasm-host.js'), scope);
    const dotnet = {invokeMethodAsync: () => { count++; return new Promise(resolve => { pending = resolve; }); }};
    scope.window.wasmWpfLoop.start(dotnet, 'canvas');
    const running = callbacks.shift()(10);
    assert.equal(count, 1); assert.equal(callbacks.length, 0);
    scope.window.wasmWpfLoop.stop(); pending(); await running;
    assert.equal(callbacks.length, 0);
});
test('a rejected .NET frame stops the loop and reports a runtime error', async () => {
    const callbacks = []; let error;
    const scope = {window: {wfwasm: {}, studioRuntime: {fail: value => { error = value; }}}, document: {getElementById: () => ({})},
        requestAnimationFrame: cb => callbacks.push(cb), ResizeObserver: class {observe() {} disconnect() {}}};
    vm.runInNewContext(await load('src/DesktopWasm.BrowserHost/wwwroot/wasm-host.js'), scope);
    scope.window.wasmWpfLoop.start({invokeMethodAsync: async () => { throw new Error('paint failed'); }}, 'canvas');
    await callbacks.shift()(10);
    assert.equal(error, 'paint failed'); assert.equal(scope.window.wasmWpfLoop.running, false); assert.equal(callbacks.length, 0);
});
test('bootstrap does not eagerly load runtime or app assemblies in the parent launcher', async () => {
    const html = await load('src/DesktopWasm.BrowserHost/wwwroot/index.html');
    assert.doesNotMatch(html, /<script[^>]+src="_framework/);
    const csproj = await load('src/DesktopWasm.BrowserHost/DesktopWasm.BrowserHost.csproj');
    assert.doesNotMatch(csproj, /EmbeddedResource Include="ConvertedApps/);
    assert.match(csproj, /wwwroot\\apps\\hello-real\\HelloWorld.bin/);
    assert.match(csproj, /wwwroot\\apps\\wpf-calculator\\CalculatorDemo.bin/);
});
test('build stages byte-identical converted inputs under the real static content root', async () => {
    const prefix = 'src/DesktopWasm.BrowserHost/';
    const original = await readFile(new URL(prefix + 'ConvertedApps/HelloWorld/HelloWorld.dll', root));
    const staged = await readFile(new URL(prefix + 'wwwroot/apps/hello-real/HelloWorld.bin', root));
    assert.deepEqual(staged, original);
    const receipt = JSON.parse(await load(prefix + 'wwwroot/apps/hello-real/HelloWorld.receipt.json'));
    assert.ok(receipt.MapVersion.startsWith('wpf-convert-'));
    assert.ok(original.length > 512);
});
test('wpf-calculator stages byte-identical converted input with a matching receipt', async () => {
    const prefix = 'src/DesktopWasm.BrowserHost/';
    const original = await readFile(new URL(prefix + 'ConvertedApps/CalculatorDemo/CalculatorDemo.dll', root));
    const staged = await readFile(new URL(prefix + 'wwwroot/apps/wpf-calculator/CalculatorDemo.bin', root));
    assert.deepEqual(staged, original);
    const receipt = JSON.parse(await load(prefix + 'wwwroot/apps/wpf-calculator/CalculatorDemo.receipt.json'));
    assert.ok(receipt.MapVersion.startsWith('wpf-convert-'));
    assert.ok(original.length > 512);
});
