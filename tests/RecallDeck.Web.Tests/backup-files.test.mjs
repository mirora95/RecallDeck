import test from "node:test";
import assert from "node:assert/strict";
import { openBackup, saveBackup } from "../../src/RecallDeck.Web/wwwroot/backup-files.js";

let created = [];
let appended = [];
class Element extends EventTarget {
    constructor(tag) { super(); this.tag = tag; }
    click() { this.clicked = true; }
    remove() { this.removed = true; }
}
function dom() {
    created = [];
    appended = [];
    globalThis.document = {
        createElement(tag) { const e = new Element(tag); created.push(e); return e; },
        body: { append(e) { appended.push(e); } }
    };
}
test("cancel resolves null and removes the file input", async () => {
    dom();
    const pending = openBackup(10);
    const input = created[0];
    assert.equal(input.type, "file");
    assert.match(input.accept, /application\/json/);
    input.dispatchEvent(new Event("cancel"));
    assert.equal(await pending, null);
    assert.equal(input.removed, true);
});
test("reads selected file only through the size sentinel and returns byte interop", async () => {
    dom();
    const pending = openBackup(3);
    const input = created[0];
    let sliceArgs;
    input.files = [{ name: "<script>.json", slice(...args) {
        sliceArgs = args;
        return { arrayBuffer: async () => new Uint8Array([1, 2, 3, 4]).buffer };
    } }];
    input.dispatchEvent(new Event("change"));

    const result = await pending;
    assert.deepEqual(sliceArgs, [0, 4]);
    assert.equal(result.name, "<script>.json");
    assert.deepEqual(result.content, new Uint8Array([1, 2, 3, 4]));
    assert.equal(input.removed, true);
});
test("file read failure rejects without leaving its element", async () => {
    dom();
    const pending = openBackup(3);
    const input = created[0];
    input.files = [{ name: "bad.json", slice() { return { arrayBuffer: async () => { throw new Error("read"); } }; } }];
    input.dispatchEvent(new Event("change"));
    await assert.rejects(pending, /read/);
    assert.equal(input.removed, true);
});
test("download uses exact JSON bytes, dated name, and revokes URL after request", () => {
    dom();
    let blob;
    let revoked;
    let callback;
    const previousTimeout = globalThis.setTimeout;
    const create = URL.createObjectURL;
    const revoke = URL.revokeObjectURL;
    URL.createObjectURL = value => { blob = value; return "blob:owned"; };
    URL.revokeObjectURL = value => { revoked = value; };
    globalThis.setTimeout = action => { callback = action; };
    try {
        assert.equal(saveBackup("recalldeck-2026-11-27.json", new Uint8Array([1, 2])), undefined);
        assert.equal(blob.type, "application/json");
        assert.equal(blob.size, 2);
        assert.equal(created[0].download, "recalldeck-2026-11-27.json");
        assert.equal(created[0].href, "blob:owned");
        assert.equal(created[0].clicked, true);
        assert.equal(created[0].removed, true);
        assert.equal(revoked, undefined);
        callback();
        assert.equal(revoked, "blob:owned");
    } finally {
        globalThis.setTimeout = previousTimeout;
        URL.createObjectURL = create;
        URL.revokeObjectURL = revoke;
    }
});
