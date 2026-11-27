import assert from 'node:assert/strict';
import test from 'node:test';
import { parseOptions } from '../../scripts/check-card-journey.mjs';
import { parseOptions as parseAccessibilityOptions } from '../../scripts/check-accessibility.mjs';

test('Accessibility driver accepts only a plain loopback origin without browser or device overrides', () => {
    assert.equal(parseAccessibilityOptions(['http://127.0.0.1:5189']).url, 'http://127.0.0.1:5189');
    for (const args of [[], ['https://example.com'], ['http://127.0.0.1:5189/path'],
        ['http://localhost:5189', '--headed'], ['android', '--serial', 'emulator-5554']]) {
        assert.throws(() => parseAccessibilityOptions(args));
    }
});

test('UI driver accepts only a plain HTTP loopback origin for a fresh headless context', () => {
    assert.equal(parseOptions(['web', 'http://127.0.0.1:5189']).url, 'http://127.0.0.1:5189');
    assert.equal(parseOptions(['web', 'http://localhost:5189']).mode, 'web');
    for (const url of ['https://example.com', 'http://127.0.0.1.evil.test', 'http://user@localhost:5189',
        'http://127.0.0.1:5189/path', 'http://127.0.0.1:5189?x=1', 'file:///tmp/index.html']) {
        assert.throws(() => parseOptions(['web', url]));
    }
    assert.throws(() => parseOptions(['web', 'http://localhost:5189', '--headed']));
});

test('Android UI driver requires a named existing emulator and permits no package override', () => {
    assert.deepEqual(parseOptions(['android', '--serial', 'emulator-5554', '--avd', 'Owned_test_avd']),
        { mode: 'android', serial: 'emulator-5554', avd: 'Owned_test_avd' });
    for (const args of [[], ['android'], ['android', '--serial', 'emulator-5554'],
        ['android', '--serial', 'device123', '--avd', 'Owned'],
        ['android', '--serial', 'emulator-5554', '--avd', 'Owned', '--package', 'io.github.mirora95.recalldeck'],
        ['android', '--serial', 'emulator-5554', '--avd', 'Owned; shell']]) {
        assert.throws(() => parseOptions(args));
    }
});
