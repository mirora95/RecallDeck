import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { parseOptions as parseJourneyOptions } from './check-card-journey.mjs';

export function parseOptions(args) {
    const options = parseJourneyOptions(['web', ...args]);
    assert.equal(args.length, 1, 'Usage: npm run test:accessibility -- http://127.0.0.1:5189');
    return options;
}

async function run({ url }) {
    const { chromium } = await import('playwright-core');
    const report = { result: 'RUNNING', node: process.version,
        playwrightCore: createRequire(import.meta.url)('playwright-core/package.json').version,
        windowsInput: false, screenshots: [], measurements: [], findings: [], checks: [] };
    const browser = await chromium.launch({ headless: true, executablePath: process.env.RECALLDECK_CHROMIUM_EXECUTABLE });
    report.browser = browser.version();
    let context;
    try {
        context = await browser.newContext({ viewport: { width: 360, height: 800 }, reducedMotion: 'reduce', serviceWorkers: 'allow' });
        await context.route('**/*', route => new URL(route.request().url()).origin === url ? route.continue() : route.abort());
        const page = await context.newPage();
        page.setDefaultTimeout(30000);
        const check = message => report.checks.push(message);

        async function visit(route) {
            await page.goto(url + route);
            await page.evaluate(() => navigator.serviceWorker.ready.then(() => true));
        }

        async function scan(state) {
            const metrics = await page.evaluate(() => {
                const visible = element => {
                    const rect = element.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0 && getComputedStyle(element).visibility !== 'hidden' && rect.bottom > 0;
                };
                const background = element => {
                    for (let node = element; node; node = node.parentElement) {
                        const value = getComputedStyle(node).backgroundColor;
                        if (value !== 'rgba(0, 0, 0, 0)' && value !== 'transparent') return value;
                    }
                    return 'rgb(255, 255, 255)';
                };
                const luminance = color => color.match(/[\d.]+/g).slice(0, 3).map(Number)
                    .map(value => value / 255).map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4)
                    .reduce((sum, value, index) => sum + value * [.2126, .7152, .0722][index], 0);
                const contrast = (a, b) => {
                    const values = [luminance(a), luminance(b)].sort((x, y) => y - x);
                    return (values[0] + .05) / (values[1] + .05);
                };
                const controls = [...document.querySelectorAll('a[href],button,input,textarea,select')]
                    .filter(element => visible(element) && !element.disabled && !element.closest('fieldset:disabled'))
                    .filter(element => !element.classList.contains('skip-link'))
                    .map(element => {
                        const rect = element.getBoundingClientRect();
                        const paragraph = element.closest('p');
                        // Sentence links retain their inline layout (WCAG 2.5.8 exception).
                        const inline = element.tagName === 'A' && paragraph && [...paragraph.childNodes]
                            .some(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim());
                        return { text: (element.getAttribute('aria-label') || element.textContent || element.id).trim().slice(0, 90),
                            width: rect.width, height: rect.height, inline: !!inline, left: rect.left, right: rect.right,
                            labelled: !element.matches('input,textarea,select') || [...element.labels].some(label => label.textContent.trim()),
                            descriptionsValid: (element.getAttribute('aria-describedby') || '').split(' ').filter(Boolean)
                                .every(id => document.getElementById(id)?.textContent.trim()) };
                    });
                const text = [...document.querySelectorAll('body *')].filter(element => visible(element) &&
                    !element.closest('button:disabled,fieldset:disabled,.visually-hidden,script,style') &&
                    [...element.childNodes].some(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim()))
                    .map(element => {
                        const style = getComputedStyle(element);
                        const large = parseFloat(style.fontSize) >= 24 || (parseFloat(style.fontSize) >= 18.666 && parseInt(style.fontWeight) >= 700);
                        return { text: element.textContent.trim().slice(0, 50), ratio: contrast(style.color, background(element)), minimum: large ? 3 : 4.5 };
                    });
                return { width: innerWidth, client: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth,
                    reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
                    motion: [...document.getAnimations()].filter(animation => animation.playState === 'running').length,
                    controls, minimumTextContrast: Math.min(...text.map(value => value.ratio)), contrastFailures: text.filter(value => value.ratio < value.minimum) };
            });
            report.measurements.push({ state, ...metrics });
            assert(metrics.scroll <= metrics.client, `${state}: horizontal overflow`);
            assert.equal(metrics.motion, 0, `${state}: unexpected active animation`);
            assert.deepEqual(metrics.contrastFailures, [], `${state}: text contrast`);
            for (const control of metrics.controls) {
                assert(control.labelled && control.descriptionsValid, `${state}: missing label or description: ${control.text}`);
                assert(control.left >= -.5 && control.right <= metrics.client + .5, `${state}: unreachable control: ${control.text}`);
                if (!control.inline && (control.width < 48 || control.height < 48)) report.findings.push({ state, ...control });
            }
        }

        async function keyboardOrder() {
            const controls = page.locator('a[href],button:not(:disabled),input:not(:disabled),textarea:not(:disabled)');
            const handles = await controls.elementHandles();
            const expected = [];
            for (const handle of handles) if (await handle.isVisible() && await handle.isEnabled()) expected.push(handle);
            await page.locator('.skip-link').focus();
            const focus = await page.locator('.skip-link').evaluate(element => ({
                outline: getComputedStyle(element).outlineStyle, width: parseFloat(getComputedStyle(element).outlineWidth),
                top: element.getBoundingClientRect().top, height: element.getBoundingClientRect().height }));
            assert(focus.outline !== 'none' && focus.width >= 2 && focus.top >= 0, 'Visible keyboard focus on skip link');
            if (focus.height < 48) report.findings.push({ state: 'focused skip link', height: focus.height });
            await page.keyboard.press('Enter');
            await page.waitForFunction(() => document.activeElement?.id === 'main');
            await page.locator('.skip-link').focus();
            for (const handle of expected.slice(1)) {
                await page.keyboard.press('Tab');
                assert(await handle.evaluate(element => element === document.activeElement), 'Tab follows the semantic DOM order');
                const style = await handle.evaluate(element => ({ style: getComputedStyle(element).outlineStyle, width: parseFloat(getComputedStyle(element).outlineWidth) }));
                assert(style.style !== 'none' && style.width >= 2, 'Each keyboard target has a focus indicator');
            }
            if (expected.length > 1) {
                await page.keyboard.press('Shift+Tab');
                assert(await expected.at(-2).evaluate(element => element === document.activeElement), 'Reverse Tab remains reachable');
            }
            for (const handle of handles) await handle.dispose();
        }

        for (const width of [360, 1280]) {
            await page.setViewportSize({ width, height: 800 });
            for (const reducedMotion of ['reduce', 'no-preference']) {
                await page.emulateMedia({ reducedMotion });
                assert.equal(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches), reducedMotion === 'reduce');
                for (const route of ['/', '/decks', '/review', '/progress', '/backup']) {
                    await visit(route);
                    await page.locator('main h1').waitFor();
                    await page.waitForFunction(() => !document.querySelector('main')?.textContent.includes('Loading'));
                    await scan(`${route} empty ${width} ${reducedMotion}`);
                    await keyboardOrder();
                }
            }
        }
        check('All five empty main routes: 360/1280 CSS px, both motion preferences, computed contrast, labels, keyboard order/reverse order and visible focus.');

        await page.setViewportSize({ width: 360, height: 800 });
        await page.emulateMedia({ reducedMotion: 'reduce' });
        await visit('/decks');
        await page.locator('#create-title').waitFor();
        await page.getByRole('button', { name: 'Create deck', exact: true }).press('Enter');
        await page.locator('#create-title-error').waitFor();
        assert.equal(await page.locator('#create-title').getAttribute('aria-invalid'), 'true');
        await scan('deck validation error');
        const title = 'Unicode 日本語 😀 ' + '字'.repeat(50);
        await page.getByLabel('Title', { exact: true }).fill(title);
        await page.getByLabel('Description', { exact: false }).fill('Я'.repeat(500));
        await page.getByRole('button', { name: 'Create deck', exact: true }).press('Space');
        const deckLink = page.locator('.deck-row h3 a');
        await deckLink.waitFor();
        await scan('long deck title and description');
        await deckLink.press('Enter');
        await page.locator('#card-front').waitFor();
        await scan('empty deck detail');
        await keyboardOrder();
        await page.getByRole('button', { name: 'Create card', exact: true }).press('Enter');
        await page.locator('#card-front-error').waitFor();
        assert.equal(await page.locator('#card-front').getAttribute('aria-invalid'), 'true');
        await scan('card validation error');
        const front = '日本語😀'.repeat(800);
        await page.getByLabel('Front', { exact: true }).fill(front);
        await page.getByLabel('Back', { exact: true }).fill('Я'.repeat(4000));
        await page.getByRole('button', { name: 'Create card', exact: true }).press('Enter');
        await page.locator('.card-row').waitFor();
        await scan('4000 UTF-16-unit Unicode card');
        await page.locator('#delete-deck').press('Enter');
        await page.waitForFunction(() => document.activeElement?.id === 'delete-heading');
        await scan('delete confirmation');
        await page.locator('#cancel-delete').press('Space');
        await page.locator('#delete-confirmation').waitFor({ state: 'detached' });
        await page.locator('#review-deck').press('Enter');
        await page.locator('#reveal-answer').waitFor();
        await scan('long review question');
        await page.locator('#reveal-answer').press('Space');
        await page.waitForFunction(() => document.activeElement?.id === 'answer-heading');
        await scan('long review answer');
        await page.keyboard.press('Tab');
        assert.equal(await page.locator('#rate-again').evaluate(element => element === document.activeElement), true);
        await page.keyboard.press('Tab');
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => document.activeElement?.id === 'complete-heading');
        await scan('review completion');
        await visit('/progress');
        await page.locator('.history-row').waitFor();
        await scan('long review history');
        await visit('/');
        await page.locator('.due-deck-row').waitFor();
        await scan('populated home');
        check('Keyboard deck/card creation, associated validation alerts, long Unicode in deck/detail/review/history/Home, reveal/completion focus and canceled deletion.');

        await visit('/backup');
        await page.locator('#choose-backup').waitFor();
        const chooser = page.waitForEvent('filechooser');
        await page.locator('#choose-backup').press('Enter');
        await (await chooser).setFiles({ name: 'invalid.json', mimeType: 'application/json', buffer: Buffer.from('{') });
        await page.getByRole('alert').waitFor();
        await scan('invalid backup error');
        const validChooser = page.waitForEvent('filechooser');
        await page.locator('#choose-backup').press('Enter');
        await (await validChooser).setFiles({ name: 'empty-日本語.json', mimeType: 'application/json',
            buffer: Buffer.from(JSON.stringify({ schemaVersion: 1, decks: [], cards: [], reviews: [] })) });
        await page.locator('#restore-counts').waitFor();
        await scan('empty backup replacement warning');
        await page.locator('#cancel-restore').press('Space');
        await page.locator('#restore-counts').waitFor({ state: 'detached' });
        assert.match(await page.locator('#backup-status').textContent(), /Restore canceled/);
        check('Backup keyboard file selection, friendly invalid-file alert, empty replacement warning and explicit cancel in a fresh synthetic browser context.');

        // Delay only owned-context IndexedDB opens: inspect the real loading state, then its recoverable failure.
        await page.evaluate(() => {
            const original = IDBFactory.prototype.open;
            window.__auditRestoreOpen = () => { IDBFactory.prototype.open = original; };
            window.__auditPendingOpen = [];
            IDBFactory.prototype.open = function () {
                const request = { error: new DOMException('synthetic audit unavailable', 'SecurityError') };
                window.__auditPendingOpen.push(request);
                return request;
            };
        });
        try {
            await page.locator('nav a[href="decks"]').press('Enter');
            await page.getByRole('status').filter({ hasText: 'Loading decks' }).waitFor();
            await scan('loading decks');
            await page.evaluate(() => window.__auditPendingOpen.forEach(request => request.onerror?.()));
            await page.getByRole('alert').waitFor();
            await scan('storage read failure');
        } finally {
            await page.evaluate(() => { window.__auditRestoreOpen(); delete window.__auditRestoreOpen; delete window.__auditPendingOpen; });
        }
        await page.getByRole('button', { name: 'Try again', exact: true }).press('Enter');
        await page.locator('.deck-row').waitFor();
        check('Controlled loading/read failure, visible recovery and keyboard retry; hook restored.');
        await visit('/missing-audit-route');
        await page.getByRole('heading', { name: 'Page not found', exact: true }).waitFor();
        await scan('not-found recovery');
        assert.deepEqual(report.findings, [], 'Standalone touch controls must provide a 48×48 CSS px area; sentence links are exempt.');
        report.result = 'PASS';
    } catch (error) {
        report.result = 'FAIL';
        report.error = String(error.stack ?? error);
        process.exitCode = 1;
    } finally {
        await context?.close();
        await browser.close();
        report.verifiedAt = new Date().toISOString();
        console.log(JSON.stringify(report, null, 2));
    }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    try { await run(parseOptions(process.argv.slice(2))); }
    catch (error) { console.error(error.message); process.exitCode = 1; }
}
