// The launcher never starts .NET. Only an app frame (or legacy demo URL) does.
// Mount-aware: the same file serves the studio root (/) and the unified-shell
// mount (/wpf/). Routing matches on the mount-stripped path so
// /wpf/runtime/<app> and /wpf/demo/<app> boot the runtime branch; asset URLs
// stay relative so the document base href scopes resource bases under either
// root without edits.
const MOUNT_PREFIX = '/wpf';
const requestPath = location.pathname;
const underMount = requestPath === MOUNT_PREFIX || requestPath.startsWith(MOUNT_PREFIX + '/');
const routePath = underMount ? (requestPath.slice(MOUNT_PREFIX.length) || '/') : requestPath;
const mountRoot = underMount ? MOUNT_PREFIX : '';
// A physical directory plus a query works on static hosts without SPA rewrites.
const staticDemo = new URLSearchParams(location.search).has('demo');
if (routePath === '/gpu-spike') {
    location.replace(mountRoot + '/?rendering=1');
} else if (staticDemo || routePath.startsWith('/runtime/') || routePath.startsWith('/demo/')) {
    document.body.classList.add('runtime-page');
    document.getElementById('studio').remove();
    const app = document.getElementById('app');
    app.hidden = false;
    app.innerHTML = '<div class="runtime-boot"><span class="spinner"></span><h2>Starting WPF runtime</h2><p class="loading-progress-text"></p></div>';
    await import('./runtime-channel.mjs');
    await script('wasm-host.js');
    await script('js/app-renderer.js');
    await script('js/wpf-dialogs.js');
    await script('js/wpf-a11y.js');
    await script('js/wpf-dragdrop.js');
    await script('js/wpf-primitives.js');
    await script('js/wpf-tabs.js');
    await script('js/wpf-hittest.js');
    await script('js/wpf-templates.js');
    await script('js/wpf-animation.js');
    await script('js/wpf-text.js');
    await script('js/wpf-gridrows.js');
    await script('js/wpf-shell.js');
    await script('js/wpf-effects.js');
    await script('js/wpf-gallery.js');
    await script('js/wpf-oxyplot.js');
    try { await script('_framework/blazor.webassembly.js'); }
    catch (error) { window.studioRuntime.fail(error.message); }
} else {
    await import('./studio.mjs');
}
function script(src) {
    return new Promise((resolve, reject) => {
        const el = document.createElement('script');
        el.src = src; el.onload = resolve; el.onerror = () => reject(new Error(`Could not load ${src}`));
        document.body.append(el);
    });
}
