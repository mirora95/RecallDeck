import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import { isDeepStrictEqual } from 'node:util';

const usage = 'Usage: npm run test:ui -- web http://127.0.0.1:5189 | android --serial emulator-5554 --avd EXPECTED_NAME';
const testPackage = 'io.github.mirora95.recalldeck.uismoke';
const failureMarker = '[RecallDeck synthetic failed save] ';

export function parseOptions(args) {
    if (args[0] === 'web' && args.length === 2) {
        const url = new URL(args[1]);
        assert(url.protocol === 'http:' && ['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname), 'Web URL must be HTTP loopback.');
        assert(!url.username && !url.password && url.pathname === '/' && !url.search && !url.hash, 'Supply only a loopback origin.');
        return { mode: 'web', url: url.origin };
    }
    if (args[0] === 'android' && args.length === 5 && args[1] === '--serial' && args[3] === '--avd') {
        assert.match(args[2], /^emulator-\d+$/, 'An existing emulator serial is required.');
        assert.match(args[4], /^[A-Za-z0-9_.-]+$/, 'The exact expected AVD name is required.');
        return { mode: 'android', serial: args[2], avd: args[4] };
    }
    throw new Error(usage);
}

async function run(options) {
    const { chromium, _android: android } = await import('playwright-core');
    const { mode } = options;
    const title = 'RecallDeck smoke ' + randomUUID();
    const description = 'Owned synthetic UI verification fixture 日本語 😀';
    const frontStart = failureMarker + 'Корт 日本語 😀 <script>window.__recalldeck_bad=true</script>\n';
    const backStart = '答え 😀 <img src=x onerror="window.__recalldeck_bad=true">\n';
    const front = frontStart + '字'.repeat(4000 - frontStart.length);
    const back = backStart + 'Я'.repeat(4000 - backStart.length);
    const editedFront = 'Edited 日本語 😀 <script>literal</script>\nQuestion';
    const editedBack = 'Answer 😀 <img src=x onerror="window.__recalldeck_bad=true">';
    const report = { mode, node: process.version, playwrightCore: createRequire(import.meta.url)('playwright-core/package.json').version, result: 'RUNNING', fixtureTitle: title, checks: [], cardIds: [], pids: [], windowsInput: false, screenshots: [] };
    let browser, context, device, page, deckPath, original;
    let expectedCardIds = [];
    const adb = (...args) => execFileSync(process.env.RECALLDECK_ADB ?? 'adb', ['-s', options.serial, ...args], { windowsHide: true, timeout: 20000, maxBuffer: 16 * 1024 * 1024 });
    const shell = (...args) => adb('shell', ...args).toString('utf8').trim();
    const check = value => report.checks.push(value);

    async function attachAndroid() {
        assert.equal(adb('emu', 'avd', 'name').toString('utf8').split(/\r?\n/)[0].trim(), options.avd);
        device = (await android.devices()).find(candidate => candidate.serial() === options.serial);
        assert(device, 'The expected emulator must already be running; this driver never launches it.');
        const webview = await device.webView({ pkg: testPackage });
        assert.equal(webview.pkg(), testPackage);
        page = await webview.page();
        assert.equal(new URL(page.url()).origin, 'https://0.0.0.1');
        page.setDefaultTimeout(30000);
        const pid = shell('pidof', testPackage);
        assert.match(pid, /^\d+$/, 'Expected exactly one test app process.');
        assert(!report.pids.includes(pid), 'Cold restart must create a different app PID.');
        report.pids.push(pid);
    }

    async function snapshot() {
        if (mode === 'web') {
            return page.evaluate(async () => {
                const module = await import('/study-store.js');
                const saved = await module.begin('read').finish();
                if (saved.error) throw new Error('Snapshot read failed: ' + saved.error);
                for (const rows of Object.values(saved)) rows.sort((a, b) => a.id.localeCompare(b.id));
                return saved;
            });
        }
        // Only the dedicated test package's app-private DB; parse in memory, never write a copy.
        const bytes = adb('exec-out', 'run-as', testPackage, 'cat', 'files/ui-smoke.db3');
        const python = process.env.RECALLDECK_PYTHON ?? (process.platform === 'win32' ? 'python' : 'python3');
        return JSON.parse(execFileSync(python, ['-c', `import sys,sqlite3,json
c=sqlite3.connect(':memory:')
c.deserialize(sys.stdin.buffer.read())
c.execute('PRAGMA query_only=ON')
c.row_factory=sqlite3.Row
print(json.dumps({t.lower():[{k[0].lower()+k[1:]:r[k] for k in r.keys()} for r in c.execute('SELECT * FROM '+t+' ORDER BY Id')] for t in ['Decks','Cards','Reviews']}))`],
        { input: bytes, encoding: 'utf8', windowsHide: true, timeout: 20000, maxBuffer: 16 * 1024 * 1024 }));
    }

    async function assertOwned() {
        assert(deckPath, 'No fixture ID: no deletion is authorized.');
        const saved = await snapshot();
        const deck = saved.decks.find(item => item.id === report.fixtureDeckId);
        assert(deck && deck.title === title && deck.description === description, 'Fixture ownership is uncertain; preserve data.');
        const cards = saved.cards.filter(item => item.deckId === deck.id);
        assert(isDeepStrictEqual(cards.map(item => item.id).sort(), [...expectedCardIds].sort()), 'Unexpected cards: stop before deleting anything.');
        const ids = new Set(expectedCardIds);
        assert(!saved.reviews.some(item => ids.has(item.cardId)), 'Unexpected review history: preserve it.');
        const withoutFixture = { decks: saved.decks.filter(item => item.id !== deck.id), cards: saved.cards.filter(item => item.deckId !== deck.id), reviews: saved.reviews };
        assert(isDeepStrictEqual(withoutFixture, original), 'Pre-existing records changed: preserve data and investigate.');
        return cards;
    }

    async function cardsEqual(expectedFront, expectedBack) {
        // Existing DOM nodes can still contain the previous value while MAUI IPC completes.
        await page.waitForFunction(({ front, back }) => document.querySelectorAll('.card-row').length === 1 &&
            document.querySelector('.card-front')?.textContent === front && document.querySelector('.card-back')?.textContent === back &&
            document.querySelector('#editor-heading')?.textContent === 'Create a card' &&
            !document.querySelector('#card-editor fieldset')?.disabled, { front: expectedFront, back: expectedBack });
        assert.equal(await page.locator('.card-row script, .card-row img').count(), 0);
        assert.notEqual(await page.evaluate(() => window.__recalldeck_bad), true);
        const saved = await snapshot();
        const cards = saved.cards.filter(item => item.deckId === report.fixtureDeckId);
        assert.equal(cards.length, 1);
        assert.equal(cards[0].front, expectedFront);
        assert.equal(cards[0].back, expectedBack);
        assert.match(cards[0].id, /^[0-9a-f-]{36}$/);
        if (!expectedCardIds.length) {
            expectedCardIds = [cards[0].id];
            report.cardIds.push(cards[0].id);
        }
        await assertOwned();
    }

    async function layout() {
        const measured = await page.evaluate(() => ({ viewport: innerWidth, client: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth }));
        assert(measured.scroll <= measured.client, JSON.stringify(measured));
        // Scroll each relevant semantic control into view and prove it stays reachable.
        for (const selector of ['#card-editor button[type="submit"]', '.edit-card', '.delete-card', '#delete-deck']) {
            const control = page.locator(selector);
            await control.scrollIntoViewIfNeeded();
            const box = await control.boundingBox();
            assert(box && box.width > 0 && box.height > 0 && box.x >= 0 && box.x + box.width <= measured.client + 1, selector);
        }
        return measured;
    }

    async function restart() {
        if (mode === 'web') {
            await page.reload();
        } else {
            await device.close();
            device = undefined;
            shell('am', 'force-stop', testPackage);
            shell('am', 'start', '-n', testPackage + '/' + testPackage + '.MainActivity');
            await attachAndroid();
            await page.getByRole('link', { name: 'Decks', exact: true }).click();
            await page.locator(`a[href='${deckPath}']`).click();
        }
        await page.getByRole('heading', { name: title, exact: true }).waitFor();
    }

    async function failedSave() {
        if (mode === 'web') {
            await page.evaluate(deckId => {
                const originalAdd = IDBObjectStore.prototype.add;
                window.__recalldeck_restore_add = () => { IDBObjectStore.prototype.add = originalAdd; delete window.__recalldeck_restore_add; };
                IDBObjectStore.prototype.add = function (value, ...args) {
                    if (this.name === 'cards' && value.deckId === deckId) throw new DOMException('synthetic UI quota failure', 'QuotaExceededError');
                    return originalAdd.call(this, value, ...args);
                };
            }, report.fixtureDeckId);
        }
        try {
            await page.getByRole('button', { name: 'Create card', exact: true }).press('Enter');
            const alert = page.locator('#card-editor [role="alert"]');
            await alert.waitFor();
            await page.waitForFunction(() => !document.querySelector('#card-editor fieldset')?.disabled);
            const error = await alert.textContent();
            assert(error.includes('We could not save your card.') && error.includes('Your text is still here.') && error.includes('retry'));
            assert(!/synthetic|SQLite|QuotaExceededError|IOException/.test(error), 'Inner failure details must stay out of the UI.');
            assert.equal(await page.locator('#card-front').inputValue(), front);
            assert.equal(await page.locator('#card-back').inputValue(), back);
            assert.equal(await page.locator('.card-row').count(), 0);
            assert.equal((await assertOwned()).length, 0);
            check(mode === 'web' ? 'Injected cards-store QuotaExceededError: friendly error, exact retained input, zero committed rows (not real disk exhaustion)' :
                'Isolated native SQLite exact-card trigger abort: friendly error, no inner details, exact retained input, zero committed rows');
        } finally {
            if (mode === 'web') await page.evaluate(() => window.__recalldeck_restore_add?.());
        }
        await page.getByRole('button', { name: 'Create card', exact: true }).click();
        await cardsEqual(front, back);
        check('One retry after removing the injected failure commits exactly one card');
    }

    async function cleanup() {
        await assertOwned();
        assert.equal(new URL(page.url()).pathname, deckPath, 'Delete only on the owned deck route.');
        await page.locator('#delete-deck').click();
        const confirmation = page.locator('#delete-confirmation');
        await confirmation.waitFor();
        const text = await confirmation.textContent();
        assert(text.includes(title) && text.includes(`${expectedCardIds.length} card`));
        await page.locator('#confirm-delete').click();
        await page.locator('#create-title').waitFor();
        await page.waitForFunction(path => !document.querySelector(`a[href='${path}']`), deckPath);
        assert(isDeepStrictEqual(await snapshot(), original), 'Pre-existing records changed after cleanup: preserve data and investigate.');
        report.cleanup = 'Owned fixture removed through confirmed UI; every pre-existing deck/card/review record preserved';
        deckPath = undefined;
    }

    try {
        if (mode === 'web') {
            browser = await chromium.launch({ headless: true, executablePath: process.env.RECALLDECK_CHROMIUM_EXECUTABLE || undefined });
            context = await browser.newContext({ viewport: { width: 1280, height: 900 }, serviceWorkers: 'block' });
            // Prevent redirects or page requests from reaching a public host.
            await context.route('**/*', route => {
                const url = new URL(route.request().url());
                return ['data:', 'blob:'].includes(url.protocol) || url.origin === options.url ? route.continue() : route.abort();
            });
            page = await context.newPage();
            page.setDefaultTimeout(30000);
            await page.goto(options.url + '/decks');
            assert.equal(new URL(page.url()).origin, options.url);
            report.browser = browser.version();
        } else {
            await attachAndroid();
            await page.getByRole('link', { name: 'Decks', exact: true }).click();
            report.package = testPackage;
            report.avd = options.avd;
        }
        await page.locator('#create-title').waitFor();
        original = await snapshot();
        await page.locator('#create-title').fill(title);
        await page.locator('#create-description').fill(description);
        await page.getByRole('button', { name: 'Create deck', exact: true }).press('Enter');
        const link = page.getByRole('link', { name: title, exact: true });
        await link.waitFor();
        deckPath = await link.getAttribute('href');
        assert.match(deckPath, /^\/decks\/[0-9a-f-]{36}$/);
        report.fixtureDeckId = deckPath.split('/').at(-1);
        await link.click();
        await page.locator('#card-front').fill(front);
        await page.locator('#card-back').fill(back);
        assert.equal((await page.locator('#card-front').inputValue()).length, 4000);
        assert.equal((await page.locator('#card-back').inputValue()).length, 4000);
        await failedSave();
        check('Both 4000 UTF-16 Unicode/HTML-like fields persist exactly, with escaped markup and no execution');
        report.layout = [await layout()];
        if (mode === 'web') {
            await page.setViewportSize({ width: 360, height: 800 });
            report.layout.push(await layout());
        }
        await page.getByRole('button', { name: 'Edit card', exact: true }).click();
        await page.waitForFunction(({ front, back }) => document.querySelector('#editor-heading')?.textContent === 'Edit card' &&
            document.querySelector('#card-front')?.value === front && document.querySelector('#card-back')?.value === back, { front, back });
        await page.locator('#card-front').fill(editedFront);
        await page.locator('#card-back').fill(editedBack);
        await page.getByRole('button', { name: 'Save card', exact: true }).click();
        await cardsEqual(editedFront, editedBack);
        await restart();
        await cardsEqual(editedFront, editedBack);
        check(mode === 'web' ? 'Edit survives actual page reload' : 'Edit survives force-stop/relaunch with a distinct test app PID');
        await page.getByRole('button', { name: 'Edit card', exact: true }).click();
        await page.waitForFunction(front => document.querySelector('#editor-heading')?.textContent === 'Edit card' && document.querySelector('#card-front')?.value === front, editedFront);
        await page.locator('#card-front').fill('Unsaved synthetic edit');
        await page.getByRole('link', { name: 'Review', exact: true }).click();
        await page.locator('#leave-confirmation').waitFor();
        assert.equal(await page.locator('#card-front').inputValue(), 'Unsaved synthetic edit');
        assert.equal(new URL(page.url()).pathname, deckPath);
        await page.locator('#stay-on-page').click();
        await page.locator('#leave-confirmation').waitFor({ state: 'detached' });
        assert.equal(await page.locator('#card-front').inputValue(), 'Unsaved synthetic edit');
        await page.getByRole('button', { name: 'Cancel edit', exact: true }).click();
        await page.locator('#leave-confirmation').waitFor();
        await page.locator('#leave-page').click();
        await cardsEqual(editedFront, editedBack);
        await page.getByRole('button', { name: 'Delete card', exact: true }).click();
        await page.locator('#delete-confirmation').waitFor();
        await page.locator('#cancel-delete').press('Space');
        await page.locator('#delete-confirmation').waitFor({ state: 'detached' });
        await restart();
        await cardsEqual(editedFront, editedBack);
        check('Dirty route navigation warns; Stay keeps exact input; explicit discard and keyboard Space cancel of card deletion preserve saved values across restart/reload');
        await assertOwned();
        await page.getByRole('button', { name: 'Delete card', exact: true }).click();
        assert((await page.locator('#delete-confirmation').textContent()).includes(editedFront));
        await page.locator('#confirm-delete').click();
        await page.waitForFunction(() => document.querySelectorAll('.card-row').length === 0 && document.querySelector('#main [role="status"]')?.textContent === 'Card deleted.');
        expectedCardIds = [];
        await assertOwned();
        check('Confirmed owned-card deletion removes its exact persisted ID');
        await page.locator('#card-front').fill(editedFront);
        await page.locator('#card-back').fill(editedBack);
        await page.getByRole('button', { name: 'Create card', exact: true }).click();
        await cardsEqual(editedFront, editedBack);
        await page.locator('#delete-deck').click();
        const confirmation = page.locator('#delete-confirmation');
        await confirmation.waitFor();
        assert((await confirmation.textContent()).includes(title) && (await confirmation.textContent()).includes('1 card'));
        await page.waitForFunction(() => document.activeElement?.id === 'delete-heading');
        await page.locator('#cancel-delete').scrollIntoViewIfNeeded();
        await page.locator('#cancel-delete').click();
        await confirmation.waitFor({ state: 'detached' });
        await restart();
        await cardsEqual(editedFront, editedBack);
        check('Deck confirmation names the exact deck and current card count; cancel survives restart/reload');
        await cleanup();
        check('Confirmed populated-deck deletion removes only owned IDs and preserves all pre-existing records');
        report.result = 'PASS';
    } catch (error) {
        report.result = 'FAIL';
        report.error = String(error.stack ?? error);
        // No speculative deletion after failure: exact fixture IDs remain available for guarded recovery.
        report.cleanup ??= 'Stopped on failure; preserve the reported fixture and inspect ownership before normal UI cleanup';
        process.exitCode = 1;
    } finally {
        report.verifiedAt = new Date().toISOString();
        try {
            if (device) await device.close();
            if (context) await context.close();
            if (browser) await browser.close();
        } finally {
            console.log(JSON.stringify(report, null, 2));
        }
    }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    if (process.argv.slice(2).join(' ') === '--help') console.log(usage);
    else {
        try { await run(parseOptions(process.argv.slice(2))); }
        catch (error) { console.error(error.message); process.exitCode = 1; }
    }
}
