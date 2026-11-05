import "fake-indexeddb/auto";
import { test } from "node:test";
import assert from "node:assert/strict";
import { cases } from "./storage-cases.mjs";

for (const event of ["error", "blocked"]) {
    test(`newer-schema verification rejects an open ${event} instead of hanging`, async () => {
        const open = indexedDB.open;
        let versionTwoOpens = 0, timer, lateConnectionClosed = false;
        const failure = new DOMException("Injected verification open failure", "UnknownError");
        try {
            indexedDB.open = function (name, version) {
                if (version !== 2 || ++versionTwoOpens !== 2) return open.call(this, name, version);
                const request = { error: failure };
                queueMicrotask(() => {
                    request[`on${event}`]?.();
                    if (event === "blocked") {
                        request.result = { close: () => { lateConnectionClosed = true; } };
                        request.onsuccess?.();
                    }
                });
                return request;
            };
            const check = cases.find(([name]) => name === "newer schema is not reset")[1];
            const timeout = new Promise((_, reject) => {
                timer = setTimeout(() => reject(new Error("Verification open never settled")), 1000);
            });
            await assert.rejects(Promise.race([check(), timeout]), event === "error" ? /Injected verification open failure/ : /blocked/i);
            if (event === "blocked") assert.equal(lateConnectionClosed, true);
        } finally {
            clearTimeout(timer);
            indexedDB.open = open;
        }
    });
}
