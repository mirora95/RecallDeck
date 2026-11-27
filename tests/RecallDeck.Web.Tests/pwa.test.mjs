import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../../src/RecallDeck.Web/wwwroot/pwa.js', import.meta.url), 'utf8');
const html = await readFile(new URL('../../src/RecallDeck.Web/wwwroot/index.html', import.meta.url), 'utf8');
const worker = await readFile(new URL('../../src/RecallDeck.Web/wwwroot/service-worker.published.js', import.meta.url), 'utf8');

function boot({ supported = true, failure = false, active = true, waiting = false } = {}) {
    const status = { textContent: '', hidden: true };
    const registration = new EventTarget();
    registration.active = active ? {} : null;
    registration.waiting = waiting ? {} : null;
    let registrations = 0;
    const navigator = supported ? { serviceWorker: {
        async register(url, options) {
            registrations++;
            assert.equal(url, 'service-worker.js');
            assert.equal(options.updateViaCache, 'none');
            if (failure) throw new Error('private details');
            return registration;
        }
    } } : {};
    const document = { getElementById(id) { assert.equal(id, 'pwa-status'); return status; } };
    vm.runInNewContext(source, { navigator, document });
    return { status, registration, registrations: () => registrations };
}

const settled = () => new Promise(resolve => setImmediate(resolve));

test('web host provides an accessible update status and loads registration module', () => {
    assert.match(html, /id="pwa-status"[^>]*role="status"/);
    assert.match(html, /<script type="module" src="pwa.js"><\/script>/);
    assert.doesNotMatch(html, /navigator.serviceWorker.register/);
});

test('unsupported service workers explain that offline use is unavailable', async () => {
    const app = boot({ supported: false });
    await settled();
    assert.equal(app.registrations(), 0);
    assert.equal(app.status.hidden, false);
    assert.match(app.status.textContent, /Offline use is not available in this browser/);
});

test('registration failure is visible and does not expose exception details', async () => {
    const app = boot({ failure: true });
    await settled();
    assert.match(app.status.textContent, /could not prepare offline use/);
    assert.match(app.status.textContent, /connect.*reopen/i);
    assert.doesNotMatch(app.status.textContent, /private details/);
});

test('an existing waiting update asks learners to save and close every tab', async () => {
    const app = boot({ waiting: true });
    await settled();
    assert.equal(app.status.hidden, false);
    assert.match(app.status.textContent, /update is ready/);
    assert.match(app.status.textContent, /Save your work/);
    assert.match(app.status.textContent, /close all RecallDeck tabs and reopen/);
});

test('installing update becomes visible only after it is ready and waiting', async () => {
    const app = boot();
    await settled();
    assert.equal(app.status.hidden, true);
    const installing = new EventTarget();
    installing.state = 'installing';
    app.registration.installing = installing;
    app.registration.dispatchEvent(new Event('updatefound'));
    installing.dispatchEvent(new Event('statechange'));
    assert.equal(app.status.hidden, true);
    installing.state = 'installed';
    app.registration.waiting = {};
    installing.dispatchEvent(new Event('statechange'));
    assert.match(app.status.textContent, /Save your work/);
});

test('first installation does not report a ready app update', async () => {
    const app = boot({ active: false });
    await settled();
    const installing = new EventTarget();
    app.registration.installing = installing;
    app.registration.dispatchEvent(new Event('updatefound'));
    installing.state = 'activated';
    installing.dispatchEvent(new Event('statechange'));
    assert.equal(app.status.hidden, true);
});

test('registration and worker keep browser-controlled activation and do not force navigation', () => {
    assert.doesNotMatch(source + worker, /skipWaiting|clients\.claim|location\.reload|controllerchange|postMessage/);
});
