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

export const cases = [
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
            const request = indexedDB.open(name, 2);
            request.onsuccess = () => { request.result.close(); resolve(); };
            request.onerror = () => reject(request.error);
        });
        equal((await read(call)).error, "version");
        const request = indexedDB.open(name, 2);
        const rows = await new Promise((resolve, reject) => {
            request.onsuccess = () => {
                const db = request.result;
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
