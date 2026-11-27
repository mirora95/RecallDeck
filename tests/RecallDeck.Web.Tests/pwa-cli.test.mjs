import assert from 'node:assert/strict';
import test from 'node:test';
import { parseOptions } from '../../scripts/check-pwa-journey.mjs';

test('PWA driver accepts only two distinct repository artifact roots', () => {
    assert.equal(parseOptions(['artifacts/pwa/wwwroot', 'artifacts/pwa-update/wwwroot']).length, 2);
    for (const args of [[], ['artifacts/pwa/wwwroot'], ['src', 'artifacts/pwa/wwwroot'],
        ['artifacts/../src', 'artifacts/pwa/wwwroot'],
        ['artifacts/pwa/wwwroot', 'artifacts/pwa/wwwroot'],
        ['artifacts/pwa/wwwroot', 'artifacts/pwa-update/wwwroot', '--headed']]) {
        assert.throws(() => parseOptions(args));
    }
});
