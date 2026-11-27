import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFile, realpath, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const repo = fileURLToPath(new URL('../', import.meta.url));
const artifacts = path.join(repo, 'artifacts');
const inside = (root, target) => {
    const relative = path.relative(root, target);
    return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative);
};

export function parseOptions(args) {
    assert.equal(args.length, 2, 'Usage: npm run test:pwa -- artifacts/pwa/wwwroot artifacts/pwa-update/wwwroot');
    const roots = args.map(value => path.resolve(repo, value));
    assert(roots.every(root => inside(artifacts, root)), 'Only repository artifact directories are allowed.');
    assert.notEqual(roots[0], roots[1], 'Supply two distinct published versions.');
    return roots;
}

async function manifest(root) {
    const text = await readFile(path.join(root, 'service-worker-assets.js'), 'utf8');
    const value = JSON.parse(text.slice(text.indexOf('{'), text.lastIndexOf('}') + 1));
    assert(value.assets.some(asset => asset.url === 'index.html'), 'Expected a published PWA asset manifest.');
    return value;
}

async function until(check, message) {
    const deadline = Date.now() + 30000;
    while (Date.now() < deadline) {
        if (await check()) return;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(message);
}

async function run(roots) {
    roots = await Promise.all(roots.map(root => realpath(root)));
    const allowed = await realpath(artifacts);
    assert(roots.every(root => inside(allowed, root)), 'Artifact symlinks must stay in repository artifacts.');
    const versions = await Promise.all(roots.map(manifest));
    assert.notEqual(versions[0].version, versions[1].version, 'Publish two genuinely different builds.');
    const report = {
        result: 'RUNNING', node: process.version,
        playwrightCore: createRequire(import.meta.url)('playwright-core/package.json').version,
        manifestVersions: versions.map(value => value.version), checks: [], screenshots: []
    };
    let current = 0;
    const mime = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json',
        '.wasm': 'application/wasm', '.css': 'text/css', '.webmanifest': 'application/manifest+json' };
    const server = createServer(async (request, response) => {
        try {
            const url = new URL(request.url, 'http://127.0.0.1');
            response.setHeader('Cache-Control', 'no-store');
            if (url.pathname === '/downloaded-backup.json') {
                response.setHeader('Content-Type', 'application/json');
                response.end('{"fixture":"synthetic backup outside the asset manifest"}');
                return;
            }
            const root = roots[current];
            const pathname = decodeURIComponent(url.pathname);
            let file = path.resolve(root, '.' + pathname);
            assert(inside(root, file) || file === root, 'Path must stay inside the owned publish.');
            if (!path.extname(pathname)) file = path.join(root, 'index.html');
            assert(inside(root, await realpath(file)), 'Asset symlinks must stay inside the owned publish.');
            assert((await stat(file)).isFile());
            response.setHeader('Content-Type', mime[path.extname(file)] ?? 'application/octet-stream');
            response.end(await readFile(file));
        } catch {
            response.statusCode = 404;
            response.end('Not found');
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const origin = 'http://127.0.0.1:' + server.address().port;
    report.origin = origin;
    let browser;
    try {
        const { chromium } = await import('playwright-core');
        browser = await chromium.launch({ headless: true, executablePath: process.env.RECALLDECK_CHROMIUM_EXECUTABLE });
        report.browser = browser.version();
        const fresh = await browser.newContext({ serviceWorkers: 'allow' });
        await fresh.setOffline(true);
        await assert.rejects(() => fresh.newPage().then(page => page.goto(origin)));
        await fresh.close();
        report.checks.push('A fresh offline browser cannot install the app.');

        const context = await browser.newContext({ serviceWorkers: 'allow', viewport: { width: 360, height: 800 } });
        await context.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort());
        let page = await context.newPage();
        page.setDefaultTimeout(30000);
        await page.goto(origin + '/decks');
        await page.locator('#create-title').waitFor();
        await page.evaluate(async () => { await navigator.serviceWorker.ready; });
        await page.reload();
        await page.waitForFunction(() => navigator.serviceWorker.controller !== null && document.querySelector('#create-title'));
        const firstIndex = await page.evaluate(() => fetch('/index.html').then(response => response.text()));
        await context.setOffline(true);
        await page.reload();
        await page.locator('#create-title').waitFor();

        const title = 'RecallDeck offline ' + randomUUID();
        await page.locator('#create-title').fill(title);
        await page.locator('#create-description').fill('Owned offline fixture 日本語 😀');
        await page.getByRole('button', { name: 'Create deck', exact: true }).click();
        await page.getByRole('link', { name: title, exact: true }).click();
        const deckPath = new URL(page.url()).pathname;
        await page.locator('#card-front').fill('Offline question 日本語 😀');
        await page.locator('#card-back').fill('Offline answer\nSecond line');
        await page.getByRole('button', { name: 'Create card', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.card-row').length === 1 &&
            document.querySelector('#card-front')?.value === '');
        await page.reload();
        await page.locator('.card-front').filter({ hasText: 'Offline question' }).waitFor();
        await page.locator('#review-deck').click();
        await page.locator('#reveal-answer').click();
        assert.equal(await page.locator('.review-answer .card-text').textContent(), 'Offline answer\nSecond line');
        await page.locator('#rate-remembered').click();
        await page.locator('#complete-heading').waitFor();
        const snapshot = () => page.evaluate(async () => {
            const store = await import('/study-store.js');
            const saved = await store.begin('read').finish();
            if (saved.error) throw new Error(saved.error);
            for (const rows of Object.values(saved)) rows.sort((a, b) => a.id.localeCompare(b.id));
            return saved;
        });
        const saved = await snapshot();
        assert.equal(saved.decks.length, 1);
        assert.equal(saved.cards.length, 1);
        assert.equal(saved.reviews.length, 1);
        assert.equal(saved.cards[0].schedule.level, 1);
        await page.reload();
        await page.getByRole('heading', { name: 'No cards due', exact: true }).waitFor();
        assert.deepEqual(await snapshot(), saved);
        report.checks.push('Installed published assets support offline reload, deck/card creation, reveal/rating and persisted review queue.');

        await page.goto(origin + '/backup');
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Export current data', exact: true }).click();
        const download = await downloadPromise;
        assert.match(download.suggestedFilename(), /^recalldeck-\d{4}-\d{2}-\d{2}\.json$/);
        await download.delete();
        await context.setOffline(false);
        assert.equal(await page.evaluate(() => fetch('/downloaded-backup.json').then(response => response.status)), 200);
        await context.setOffline(true);
        assert.equal(await page.evaluate(() => fetch('/downloaded-backup.json').then(() => false, () => true)), true);
        const assertCache = async version => {
            const cached = await page.evaluate(async name => {
                const cache = await caches.open(name);
                return (await cache.keys()).map(request => new URL(request.url).pathname.slice(1)).sort();
            }, 'offline-cache-' + version.version);
            const expected = version.assets.filter(asset => asset.url !== 'service-worker.js').map(asset => asset.url).sort();
            assert.deepEqual(cached, expected, 'Cache contains only the published manifest assets.');
        };
        await assertCache(versions[0]);
        report.checks.push('Offline backup download works; a fetched arbitrary JSON backup never enters the manifest-only cache.');
        await context.setOffline(false);
        assert.deepEqual(await snapshot(), saved);

        await page.goto(origin + deckPath);
        await page.locator('#card-front').fill('Unsaved front 日本語 😀 <script>literal</script>');
        await page.locator('#card-back').fill('Unsaved back\nExact second line');
        const second = await context.newPage();
        second.setDefaultTimeout(30000);
        await second.goto(origin + '/decks');
        await second.locator('#create-title').fill('Unsaved deck title');
        await second.locator('#create-description').fill('Unsaved description\n日本語 😀');
        let navigations = 0;
        page.on('framenavigated', () => navigations++);
        second.on('framenavigated', () => navigations++);
        current = 1;
        await page.evaluate(async () => { await (await navigator.serviceWorker.getRegistration()).update(); });
        for (const tab of [page, second]) {
            await tab.waitForFunction(() => document.querySelector('#pwa-status')?.textContent.includes('update is ready'));
            assert.match(await tab.locator('#pwa-status').textContent(), /Save your work, then close all RecallDeck tabs and reopen/);
            assert.equal(await tab.locator('#pwa-status').getAttribute('role'), 'status');
            assert.equal(await tab.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
            await tab.waitForFunction(async () => {
                const registration = await navigator.serviceWorker.getRegistration();
                return registration.waiting?.state === 'installed' && registration.active?.state === 'activated';
            });
        }
        assert.equal(await page.locator('#card-front').inputValue(), 'Unsaved front 日本語 😀 <script>literal</script>');
        assert.equal(await page.locator('#card-back').inputValue(), 'Unsaved back\nExact second line');
        assert.equal(await second.locator('#create-title').inputValue(), 'Unsaved deck title');
        assert.equal(await second.locator('#create-description').inputValue(), 'Unsaved description\n日本語 😀');
        assert.equal(await page.evaluate(() => fetch('/index.html').then(response => response.text())), firstIndex);
        assert.equal(navigations, 0, 'Installing an update must not reload or navigate an open tab.');
        assert.deepEqual(await snapshot(), saved);
        await assertCache(versions[1]);
        report.checks.push('A second real publish waits, warns both 360px tabs and retains every exact unsaved field without navigation.');

        await page.getByRole('button', { name: 'Create card', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('.card-row').length === 2 &&
            document.querySelector('#card-front')?.value === '');
        const afterSave = await snapshot();
        assert.equal(afterSave.cards.length, 2);
        assert.deepEqual(afterSave.reviews, saved.reviews);
        let updatedWorker;
        for (const worker of context.serviceWorkers()) {
            if (await worker.evaluate(() => self.assetsManifest.version) === versions[1].version) updatedWorker = worker;
        }
        assert(updatedWorker, 'The waiting worker must run the second manifest.');
        await page.close();
        assert.equal(await second.locator('#create-title').inputValue(), 'Unsaved deck title');
        assert.equal(await second.evaluate(async () => (await navigator.serviceWorker.getRegistration()).waiting?.state), 'installed');
        await second.close();
        await until(() => updatedWorker.evaluate(() => self.registration.active?.state === 'activated' &&
            self.registration.waiting === null), 'Closing all owned tabs did not activate the waiting version.');
        page = await context.newPage();
        page.setDefaultTimeout(30000);
        await context.setOffline(true);
        await page.goto(origin + deckPath);
        await page.waitForFunction(() => document.querySelectorAll('.card-row').length === 2);
        assert.deepEqual(await snapshot(), afterSave);
        assert.deepEqual(await page.evaluate(() => caches.keys()), ['offline-cache-' + versions[1].version]);
        await page.waitForFunction(() => document.querySelector('#pwa-status')?.hidden === true);
        await context.setOffline(false);
        assert.deepEqual(await snapshot(), afterSave);
        report.checks.push('Closing only one tab keeps the update waiting; closing all activates it naturally, and an offline reopen preserves saved rows.');
        report.result = 'PASS';
    } catch (error) {
        report.result = 'FAIL';
        report.error = error.message;
        process.exitCode = 1;
    } finally {
        try { await browser?.close(); }
        finally { await new Promise(resolve => server.close(resolve)); }
        report.cleanup = 'Owned headless browser contexts/downloads and loopback server closed; no screenshots created.';
        console.log(JSON.stringify(report, null, 2));
    }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
    try { await run(parseOptions(process.argv.slice(2))); }
    catch (error) { console.error(error.message); process.exitCode = 1; }
}
