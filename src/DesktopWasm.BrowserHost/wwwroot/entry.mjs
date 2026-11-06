// The launcher never starts .NET. Only an app frame (or legacy demo URL) does.
const mount = '/wpf';
const route = location.pathname.startsWith(mount + '/')
    ? location.pathname.slice(mount.length) : location.pathname;
if (route === '/gpu-spike') {
    location.replace('/?rendering=1');
} else if (route.startsWith('/runtime/') || route.startsWith('/demo/')) {
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
