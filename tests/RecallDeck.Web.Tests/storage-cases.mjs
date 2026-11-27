import { start } from "../../src/RecallDeck.Web/wwwroot/study-store.js";

const now = "2026-11-04T14:00:00+00:00";
const schedule = { level: 0, dueAt: now };
const deck = { id: "deck-a", title: "日本語 😀", description: "two\nlines" };
const card = { id: "card-a", deckId: deck.id, front: "<script>literal</script>", back: "answer", schedule };
const review = { id: "review-a", cardId: card.id, sessionItemId: "session-a", rating: 1, reviewedAt: now };
const next = { level: 1, dueAt: "2026-11-05T14:00:00+00:00" };
const equal = (a, b) => { if (JSON.stringify(a) !== JSON.stringify(b)) throw new Error(`Expected ${JSON.stringify(b)}, got ${JSON.stringify(a)}`); };

async function isolated(check) {
    const name = `RecallDeck-test-${crypto.randomUUID()}`;
    const call = async (operation, value, schedule) => start(name, operation, value, schedule).finish();
    try { await check(call, name); }
    finally {
        await new Promise((resolve, reject) => {
            const request = indexedDB.deleteDatabase(name);
            request.onsuccess = resolve;
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error("Test database connection leaked"));
        });
    }
}

async function seed(call) {
    equal(await call("createDeck", deck), {});
    equal(await call("createCard", card), {});
}

const read = call => call("read");
const reject = async (call, operation, value, code, schedule) => equal((await call(operation, value, schedule)).error, code);

async function openFixture(name, version, initialize) {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(name, version);
        let abandoned = false;
        request.onupgradeneeded = () => initialize?.(request.result, request.transaction);
        request.onerror = () => reject(request.error);
        request.onblocked = () => { abandoned = true; reject(new Error("Test database verification blocked")); };
        request.onsuccess = () => {
            if (abandoned) { request.result.close(); return; }
            resolve(request.result);
        };
    });
}

async function versionOne(name, change = () => {}, layout = () => {}) {
    const db = await openFixture(name, 1, (db, tx) => {
        for (const name of ["decks", "cards", "reviews"]) db.createObjectStore(name, { keyPath: "id" });
        tx.objectStore("reviews").createIndex("sessionItemId", "sessionItemId", { unique: true });
        const snapshot = { decks: [deck], cards: [{ ...card, schedule: next }], reviews: [review] };
        change(snapshot);
        for (const [name, rows] of Object.entries(snapshot)) for (const row of rows) tx.objectStore(name).add(row);
        layout(db, tx);
    });
    db.close();
}

async function fixtureSnapshot(name) {
    const db = await openFixture(name);
    try {
        return await new Promise((resolve, reject) => {
            const snapshot = { version: db.version };
            const tx = db.transaction(["decks", "cards", "reviews"]);
            for (const name of ["decks", "cards", "reviews"]) {
                const request = tx.objectStore(name).getAll();
                request.onsuccess = () => { snapshot[name] = request.result; };
            }
            tx.oncomplete = () => resolve(snapshot);
            tx.onabort = () => reject(tx.error);
        });
    } finally { db.close(); }
}

export const cases = [
    ["version 1 upgrades preserve all fields and reopen at version 2", () => isolated(async (call, name) => {
        await versionOne(name);
        const before = await fixtureSnapshot(name);

        equal(await read(call), { decks: before.decks, cards: before.cards, reviews: before.reviews });
        equal(await fixtureSnapshot(name), { ...before, version: 2 });
        equal(await read(call), { decks: before.decks, cards: before.cards, reviews: before.reviews });
    })],
    ["upgrade request failure rolls back and allows retry", () => isolated(async (call, name) => {
        await versionOne(name);
        const before = await fixtureSnapshot(name);
        const getAll = IDBObjectStore.prototype.getAll;
        const open = indexedDB.open;
        let rollbackFinished = false;
        try {
            indexedDB.open = function (...args) {
                const request = open.apply(this, args);
                request.addEventListener("error", () => { rollbackFinished = true; });
                return request;
            };
            IDBObjectStore.prototype.getAll = function (...args) {
                // A duplicate request fails asynchronously after the upgrade has started.
                if (this.transaction.mode === "versionchange" && this.name === "cards") return this.add(card);
                return getAll.apply(this, args);
            };
            equal((await read(call)).error, "migration");
            equal(rollbackFinished, true);
        } finally {
            IDBObjectStore.prototype.getAll = getAll;
            indexedDB.open = open;
        }

        equal(await fixtureSnapshot(name), before);
        equal((await read(call)).cards, before.cards);
        equal((await fixtureSnapshot(name)).version, 2);
    })],
    ["malformed version 1 card rolls back and repaired data can retry", () => isolated(async (call, name) => {
        await versionOne(name, snapshot => { delete snapshot.cards[0].schedule; });
        const before = await fixtureSnapshot(name);

        equal((await read(call)).error, "migration");
        equal(await fixtureSnapshot(name), before);

        const db = await openFixture(name, 1);
        await new Promise((resolve, reject) => {
            const tx = db.transaction("cards", "readwrite");
            tx.objectStore("cards").put({ ...card, schedule: next });
            tx.oncomplete = resolve;
            tx.onabort = () => reject(tx.error);
        }).finally(() => db.close());
        equal((await read(call)).cards, [{ ...card, schedule: next }]);
        equal((await fixtureSnapshot(name)).version, 2);
    })],
    ["cancel during upgrade rolls back before settling and permits retry", () => isolated(async (call, name) => {
        await versionOne(name);
        const before = await fixtureSnapshot(name);
        const getAll = IDBObjectStore.prototype.getAll;
        let pending;
        try {
            IDBObjectStore.prototype.getAll = function (...args) {
                const request = getAll.apply(this, args);
                if (this.transaction.mode === "versionchange") request.addEventListener("success", () => pending.cancel());
                return request;
            };
            pending = start(name, "deleteDeck", deck.id);
            equal((await pending.finish()).error, "canceled");
        } finally { IDBObjectStore.prototype.getAll = getAll; }

        equal(await fixtureSnapshot(name), before);
        equal((await read(call)).reviews, [review]);
    })],
    ["cancel after upgrade commit prevents the pending delete", () => isolated(async (call, name) => {
        await versionOne(name);
        const before = await fixtureSnapshot(name);
        const open = indexedDB.open;
        let pending;
        try {
            indexedDB.open = function (...args) {
                const request = open.apply(this, args);
                request.addEventListener("upgradeneeded", () => {
                    request.transaction.addEventListener("complete", () => pending.cancel());
                });
                return request;
            };
            pending = start(name, "deleteDeck", deck.id);
            equal(await pending.finish(), { error: "canceled" });
        } finally { indexedDB.open = open; }

        equal(await fixtureSnapshot(name), { ...before, version: 2 });
        equal(await read(call), { decks: before.decks, cards: before.cards, reviews: before.reviews });
        equal(await call("deleteDeck", deck.id), {});
        equal(await read(call), { decks: [], cards: [], reviews: [] });
    })],
    ["cancel at requested mutation commit reports its successful outcome", () => isolated(async (call, name) => {
        await seed(call);
        const transaction = IDBDatabase.prototype.transaction;
        let pending;
        try {
            IDBDatabase.prototype.transaction = function (...args) {
                const tx = transaction.apply(this, args);
                tx.addEventListener("complete", () => pending.cancel());
                return tx;
            };
            pending = start(name, "deleteDeck", deck.id);
            equal(await pending.finish(), {});
        } finally { IDBDatabase.prototype.transaction = transaction; }

        equal(await read(call), { decks: [], cards: [], reviews: [] });
    })],
    ["empty database and stable snapshots across create/update/reopen", () => isolated(async call => {
        equal(await read(call), { decks: [], cards: [], reviews: [] });
        await seed(call);
        const before = await read(call);
        equal(await call("updateDeck", { ...deck, title: "Renamed" }), {});
        equal(await call("updateCard", { ...card, front: "Edited" }), {});
        equal(before, { decks: [deck], cards: [card], reviews: [] });
        equal(await read(call), { decks: [{ ...deck, title: "Renamed" }], cards: [{ ...card, front: "Edited" }], reviews: [] });
    })],
    ["missing, duplicate and orphan mutations preserve state", () => isolated(async call => {
        await seed(call);
        const before = await read(call);
        await reject(call, "createDeck", deck, "duplicate");
        await reject(call, "createCard", card, "duplicate");
        await reject(call, "updateDeck", { ...deck, id: "missing" }, "missing");
        await reject(call, "deleteDeck", "missing", "missing");
        await reject(call, "createCard", { ...card, id: "other", deckId: "missing" }, "missing");
        await reject(call, "updateCard", { ...card, id: "missing" }, "missing");
        await reject(call, "deleteCard", "missing", "missing");
        await reject(call, "applyReview", { ...review, cardId: "missing" }, "missing", next);
        equal(await read(call), before);
    })],
    ["card content edits preserve deck and schedule", () => isolated(async call => {
        await seed(call);
        const before = await read(call);
        await reject(call, "updateCard", { ...card, deckId: "different" }, "invalid");
        await reject(call, "updateCard", { ...card, schedule: next }, "invalid");
        equal(await read(call), before);
    })],
    ["review persists together and duplicate session cannot rate twice", () => isolated(async call => {
        await seed(call);
        equal(await call("applyReview", review, next), {});
        const after = { decks: [deck], cards: [{ ...card, schedule: next }], reviews: [review] };
        equal(await read(call), after);
        await reject(call, "applyReview", { ...review, sessionItemId: "other" }, "duplicate", schedule);
        await reject(call, "applyReview", { ...review, id: "other" }, "duplicate", schedule);
        equal(await read(call), after);
    })],
    ["empty deck deletion retains other cards and history", () => isolated(async call => {
        await seed(call);
        await call("applyReview", review, next);
        const empty = { ...deck, id: "empty" };
        await call("createDeck", empty);
        const before = await read(call);

        equal(await call("deleteDeck", empty.id), {});

        equal(await read(call), { decks: [deck], cards: before.cards, reviews: before.reviews });
    })],
    ["card and deck deletion cascade history and retain unrelated data", () => isolated(async call => {
        await seed(call);
        await call("applyReview", review, next);
        const other = { ...deck, id: "other" };
        await call("createDeck", other);
        await call("deleteCard", card.id);
        equal(await read(call), { decks: [deck, other], cards: [], reviews: [] });
        await call("createCard", card);
        await call("applyReview", review, next);
        await call("deleteDeck", deck.id);
        equal(await read(call), { decks: [other], cards: [], reviews: [] });
    })],
    ["failed second write rolls back schedule and allows same review retry", () => isolated(async call => {
        await seed(call);
        const before = await read(call);
        const add = IDBObjectStore.prototype.add;
        try {
            IDBObjectStore.prototype.add = function (...args) {
                if (this.name === "reviews") throw new DOMException("Injected quota", "QuotaExceededError");
                return add.apply(this, args);
            };
            await reject(call, "applyReview", review, "quota", next);
        } finally { IDBObjectStore.prototype.add = add; }
        equal(await read(call), before);
        equal(await call("applyReview", review, next), {});
        equal((await read(call)).reviews, [review]);
    })],
    ["asynchronous request error rolls back an earlier successful write", () => isolated(async call => {
        await seed(call);
        await call("applyReview", review, next);
        const before = await read(call);
        const add = IDBObjectStore.prototype.add;
        try {
            IDBObjectStore.prototype.add = function (value) {
                return add.call(this, this.name === "reviews" ? review : value);
            };
            await reject(call, "applyReview", { ...review, id: "other", sessionItemId: "other" }, "duplicate", schedule);
        } finally { IDBObjectStore.prototype.add = add; }
        equal(await read(call), before);
    })],
    ["concurrent connections do not overwrite unrelated commits", () => isolated(async call => {
        const results = await Promise.all(Array.from({ length: 8 }, (_, i) => call("createDeck", { ...deck, id: `deck-${i}` })));
        equal(results, Array.from({ length: 8 }, () => ({})));
        equal((await read(call)).decks.length, 8);
    })],
    ["unavailable storage is actionable and retry preserves existing data", () => isolated(async call => {
        await seed(call);
        const open = indexedDB.open;
        try {
            indexedDB.open = () => { throw new DOMException("Blocked", "SecurityError"); };
            const result = await read(call);
            equal(result.error, "unavailable");
        } finally { indexedDB.open = open; }
        equal((await read(call)).cards, [card]);
    })],
    ["newer schema is not reset", () => isolated(async (call, name) => {
        await seed(call);
        await new Promise((resolve, reject) => {
            const request = indexedDB.open(name, 3);
            request.onsuccess = () => { request.result.close(); resolve(); };
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error("Test database upgrade blocked"));
        });
        equal((await read(call)).error, "version");
        const request = indexedDB.open(name, 3);
        const rows = await new Promise((resolve, reject) => {
            let abandoned = false;
            request.onerror = () => reject(request.error);
            request.onblocked = () => { abandoned = true; reject(new Error("Test database verification blocked")); };
            request.onsuccess = () => {
                const db = request.result;
                if (abandoned) { db.close(); return; }
                const tx = db.transaction("cards");
                const get = tx.objectStore("cards").getAll();
                tx.oncomplete = () => { db.close(); resolve(get.result); };
                tx.onabort = () => { db.close(); reject(tx.error); };
            };
        });
        equal(rows, [card]);
    })],
    ["cancel while opening leaves no partial write", () => isolated(async (call, name) => {
        await seed(call);
        const pending = start(name, "deleteDeck", deck.id);
        pending.cancel();
        equal((await pending.finish()).error, "canceled");
        equal((await read(call)).cards, [card]);
    })],
    ["cancel after both review requests succeed rolls back and permits retry", () => isolated(async (call, name) => {
        await seed(call);
        const before = await read(call);
        const add = IDBObjectStore.prototype.add;
        let pending;
        try {
            IDBObjectStore.prototype.add = function (...args) {
                const request = add.apply(this, args);
                if (this.name === "reviews") request.addEventListener("success", () => pending.cancel());
                return request;
            };
            pending = start(name, "applyReview", review, next);
            equal((await pending.finish()).error, "canceled");
        } finally { IDBObjectStore.prototype.add = add; }
        equal(await read(call), before);
        equal(await call("applyReview", review, next), {});
    })],
    ["cancel after commit reports the actual successful outcome", () => isolated(async (call, name) => {
        const pending = start(name, "createDeck", deck);
        equal(await pending.finish(), {});
        pending.cancel();
        equal(await pending.finish(), {});
        equal((await read(call)).decks, [deck]);
    })]
];

for (const operation of ["createDeck", "updateDeck", "deleteDeck", "createCard", "updateCard", "deleteCard", "applyReview"]) {
    cases.push([`${operation}: cancellation inside transaction rolls back`, () => isolated(async (call, name) => {
        await seed(call);
        const before = await read(call);
        const getAll = IDBObjectStore.prototype.getAll;
        let pending;
        try {
            IDBObjectStore.prototype.getAll = function (...args) {
                const request = getAll.apply(this, args);
                request.addEventListener("success", () => pending.cancel());
                return request;
            };
            const value = operation === "applyReview" ? review : operation.endsWith("Deck") ? (operation.startsWith("delete") ? deck.id : { ...deck, id: operation.startsWith("create") ? "new" : deck.id }) : operation.startsWith("delete") ? card.id : { ...card, id: operation.startsWith("create") ? "new" : card.id };
            pending = start(name, operation, value, next);
            equal((await pending.finish()).error, "canceled");
        } finally { IDBObjectStore.prototype.getAll = getAll; }
        equal(await read(call), before);
    })]);
}

for (const [field, values] of [["level", [-1, 6, 1.5, "1", null]], ["rating", [-1, 2, 0.5, "1", null]]]) {
    for (const value of values) {
        cases.push([`version 1 invalid ${field} ${JSON.stringify(value)} preserves records and version`, () => isolated(async (call, name) => {
            await versionOne(name, snapshot => {
                if (field === "level") snapshot.cards[0].schedule.level = value;
                else snapshot.reviews[0].rating = value;
            });
            const before = await fixtureSnapshot(name);

            equal((await read(call)).error, "migration");
            equal(await fixtureSnapshot(name), before);
        })]);
        cases.push([`writes reject invalid ${field} ${JSON.stringify(value)} atomically`, () => isolated(async call => {
            await seed(call);
            const before = await read(call);

            if (field === "level") {
                await reject(call, "createCard", { ...card, id: "other", schedule: { ...next, level: value } }, "invalid");
                await reject(call, "applyReview", review, "invalid", { ...next, level: value });
            } else await reject(call, "applyReview", { ...review, rating: value }, "invalid", next);
            equal(await read(call), before);
        })]);
    }
}

for (const [label, layout] of [
    ["extra store", db => db.createObjectStore("unknown", { keyPath: "id" })],
    ["missing store", db => db.deleteObjectStore("decks")],
    ["wrong key", db => { db.deleteObjectStore("decks"); db.createObjectStore("decks", { keyPath: "other" }); }],
    ["missing index", (_, tx) => tx.objectStore("reviews").deleteIndex("sessionItemId")],
    ["nonunique index", (_, tx) => { const reviews = tx.objectStore("reviews"); reviews.deleteIndex("sessionItemId"); reviews.createIndex("sessionItemId", "sessionItemId"); }],
    ["wrong index key", (_, tx) => { const reviews = tx.objectStore("reviews"); reviews.deleteIndex("sessionItemId"); reviews.createIndex("sessionItemId", "other", { unique: true }); }]
]) {
    cases.push([`unknown version 1 layout: ${label} is preserved`, () => isolated(async (call, name) => {
        await versionOne(name, undefined, layout);
        const db = await openFixture(name);
        const original = [...db.objectStoreNames];
        db.close();

        equal((await read(call)).error, "migration");
        const preserved = await openFixture(name);
        try { equal(preserved.version, 1); equal([...preserved.objectStoreNames], original); }
        finally { preserved.close(); }
    })]);
}
