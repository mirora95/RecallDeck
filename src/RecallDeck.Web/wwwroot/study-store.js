const stores = ["decks", "cards", "reviews"];
const validLevel = value => Number.isInteger(value) && value >= 0 && value <= 5;
const validRating = value => Number.isInteger(value) && value >= 0 && value <= 1;

export function begin(operation, value, schedule) {
    return start("RecallDeck", operation, value, schedule);
}

export function start(databaseName, operation, value, schedule) {
    let db, transaction, canceled = false, settled = false, upgrading = false;
    let complete;
    const result = new Promise(resolve => { complete = resolve; });
    const finish = value => {
        if (settled) return;
        settled = true;
        db?.close();
        complete(value);
    };
    const errorCode = error => error?.code && typeof error.code === "string" ? error.code : ({
        QuotaExceededError: "quota", SecurityError: "unavailable", InvalidStateError: "unavailable",
        NotSupportedError: "unavailable", VersionError: "version", ConstraintError: "duplicate"
    }[error?.name] ?? "storage");

    try {
        if (!globalThis.indexedDB) throw new DOMException("IndexedDB unavailable", "NotSupportedError");
        const open = indexedDB.open(databaseName, 2);
        open.onupgradeneeded = event => {
            db = open.result;
            transaction = open.transaction;
            upgrading = true;
            try {
                if (canceled || settled) { transaction.abort(); return; }
                if (event.oldVersion === 0) {
                    for (const name of stores) db.createObjectStore(name, { keyPath: "id" });
                    transaction.objectStore("reviews").createIndex("sessionItemId", "sessionItemId", { unique: true });
                } else {
                    if (event.oldVersion !== 1 || db.objectStoreNames.length !== stores.length ||
                        stores.some(name => !db.objectStoreNames.contains(name))) throw new Error("Unknown study layout");
                    for (const name of stores) {
                        const store = transaction.objectStore(name);
                        if (store.keyPath !== "id" || store.autoIncrement) throw new Error("Unknown study key");
                    }
                    const reviews = transaction.objectStore("reviews");
                    const index = reviews.index("sessionItemId");
                    if (index.keyPath !== "sessionItemId" || !index.unique || index.multiEntry)
                        throw new Error("Unknown review session index");
                    for (const [name, valid] of [["cards", row => validLevel(row.schedule?.level)], ["reviews", row => validRating(row.rating)]]) {
                        const request = transaction.objectStore(name).getAll();
                        request.onsuccess = () => {
                            if (request.result.some(row => !valid(row))) transaction.abort();
                        };
                    }
                }
            } catch { transaction.abort(); }
            // Open's error event follows upgrade rollback; only then may finish report failure.
        };
        open.onblocked = () => finish({ error: "storage" });
        open.onerror = () => finish({ error: canceled ? "canceled" : upgrading ? "migration" : errorCode(open.error) });
        open.onsuccess = () => {
            db = open.result;
            if (settled || canceled) { db.close(); finish({ error: "canceled" }); return; }
            db.onversionchange = () => db.close();

            transaction = undefined;
            let failure, snapshot, remaining = stores.length;
            try {
                transaction = db.transaction(stores, operation === "read" ? "readonly" : "readwrite");
                transaction.oncomplete = () => finish(operation === "read" ? snapshot : {});
                transaction.onabort = () => finish({ error: canceled ? "canceled" : errorCode(failure ?? transaction.error) });
                transaction.onerror = event => { failure ??= event.target.error; };
                snapshot = {};
                for (const name of stores) {
                    const request = transaction.objectStore(name).getAll();
                    request.onsuccess = () => {
                        snapshot[name] = request.result;
                        if (--remaining !== 0 || operation === "read") return;
                        try { mutate(transaction, snapshot, operation, value, schedule); }
                        catch (error) { failure = error; transaction.abort(); }
                    };
                }
            } catch (error) {
                if (transaction) { failure = error; transaction.abort(); }
                else finish({ error: errorCode(error) });
            }
        };
    } catch (error) { finish({ error: errorCode(error) }); }

    return {
        finish: () => result,
        cancel: () => {
            if (settled) return;
            if (!transaction) { canceled = true; return; }
            try { transaction.abort(); canceled = true; }
            catch (error) {
                // A completed upgrade must not start a canceled mutation; a committed mutation keeps its real outcome.
                if (error.name !== "InvalidStateError") throw error;
                if (transaction.mode === "versionchange") canceled = true;
            }
        }
    };
}

function mutate(tx, snapshot, operation, value, schedule) {
    const { decks, cards, reviews } = snapshot;
    const store = name => tx.objectStore(name);
    const fail = code => { throw Object.assign(new Error(code), { code }); };
    const require = (rows, id) => rows.find(row => row.id === id) ?? fail("missing");
    const unique = (rows, id) => { if (rows.some(row => row.id === id)) fail("duplicate"); };
    const deleteHistory = id => {
        for (const review of reviews) if (review.cardId === id) store("reviews").delete(review.id);
    };

    switch (operation) {
        case "createDeck":
            unique(decks, value.id);
            store("decks").add(value);
            break;
        case "updateDeck":
            require(decks, value.id);
            store("decks").put(value);
            break;
        case "deleteDeck":
            require(decks, value);
            for (const card of cards) {
                if (card.deckId !== value) continue;
                deleteHistory(card.id);
                store("cards").delete(card.id);
            }
            store("decks").delete(value);
            break;
        case "createCard":
            if (!validLevel(value.schedule?.level)) fail("invalid");
            require(decks, value.deckId);
            unique(cards, value.id);
            store("cards").add(value);
            break;
        case "updateCard": {
            const current = require(cards, value.id);
            if (current.deckId !== value.deckId || current.schedule.level !== value.schedule.level || current.schedule.dueAt !== value.schedule.dueAt) fail("invalid");
            store("cards").put(value);
            break;
        }
        case "deleteCard":
            require(cards, value);
            deleteHistory(value);
            store("cards").delete(value);
            break;
        case "applyReview": {
            if (!validLevel(schedule?.level) || !validRating(value.rating)) fail("invalid");
            const current = require(cards, value.cardId);
            unique(reviews, value.id);
            if (reviews.some(review => review.sessionItemId === value.sessionItemId)) fail("duplicate");
            store("cards").put({ ...current, schedule });
            store("reviews").add(value);
            break;
        }
        default: fail("storage");
    }
}
