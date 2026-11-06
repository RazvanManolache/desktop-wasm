// Pure contracts shared by the launcher and its regression tests.
export function searchApps(apps, {query = '', filter = 'all', category = 'all'} = {}) {
    const words = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    return apps.filter(app =>
        (filter !== 'ready' || app.status === 'ready') &&
        (filter !== 'external' || app.kind === 'external') &&
        (filter !== 'toolkit' || app.kind === 'toolkit') &&
        (category === 'all' || app.category === category) &&
        words.every(word => `${app.label} ${app.description} ${app.category} ${app.author || ''}`.toLocaleLowerCase().includes(word)));
}
export function readRoute(path, basePath = '') {
    basePath = basePath.replace(/\/$/, '');
    if (basePath) {
        if (path === basePath) path = '/';
        else if (path.startsWith(basePath + '/')) path = path.slice(basePath.length);
        else return {unknown: path};
    }
    const match = /^\/app\/([^/]+)\/?$/.exec(path);
    if (!match) return path === '/' ? null : {unknown: path};
    try { return {slug: decodeURIComponent(match[1])}; }
    catch { return {unknown: path}; }
}
export function resourceTotals(entries) {
    return entries.reduce((total, entry) => {
        total.loadedBytes += Math.max(0, entry.decodedBodySize || 0);
        total.networkBytes += Math.max(0, entry.transferSize || 0);
        // Timing reports reused responses, not whether reuse was memory or disk.
        if (entry.encodedBodySize > 0 && entry.transferSize === 0) total.reusedResponses++;
        total.requests++;
        return total;
    }, {loadedBytes: 0, networkBytes: 0, reusedResponses: 0, requests: 0});
}
export function acceptsMessage(event, {origin, source, session, slug}) {
    const d = event.data;
    return event.origin === origin && event.source === source && !!source &&
        d?.channel === 'wpf-studio' && d.session === session && d.slug === slug &&
        ['progress', 'ready', 'error', 'metrics', 'screenshot', 'heartbeat', 'renderer'].includes(d.type);
}
export class Sessions {
    constructor() { this.current = null; this.sequence = 0; }
    start(slug, token) {
        this.current = {slug, token, generation: ++this.sequence, status: 'loading'};
        return this.current;
    }
    update(token, status) {
        if (!this.current || this.current.token !== token ||
            ['stopped', 'failed'].includes(this.current.status)) return false;
        this.current.status = status;
        return true;
    }
    stop() { if (this.current) this.current.status = 'stopped'; }
}
export function validateCatalog(apps) {
    const seen = new Set();
    for (const app of apps) {
        if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(app.slug) || seen.has(app.slug)) throw new Error('Invalid or duplicate app slug');
        seen.add(app.slug);
        if (!app.label || !app.description || !app.category || !['toolkit', 'external', 'diagnostic'].includes(app.kind) ||
            !['ready', 'not-converted'].includes(app.status)) throw new Error(`Incomplete app: ${app.slug}`);
        if (app.kind === 'external' && (!app.source?.startsWith('https://github.com/') || !app.license))
            throw new Error(`External source and license required: ${app.slug}`);
        if (app.status !== 'ready' && !app.boundary) throw new Error(`Compatibility boundary required: ${app.slug}`);
    }
    return apps;
}
