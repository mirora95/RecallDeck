export function openBackup(maximumBytes) {
    return new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.id = "backup-file-input";
        input.type = "file";
        input.accept = ".json,application/json";
        input.hidden = true;
        input.addEventListener("cancel", () => {
            input.remove();
            resolve(null);
        }, { once: true });
        input.addEventListener("change", async () => {
            try {
                const file = input.files?.[0];
                if (!file) { resolve(null); return; }
                // Read one sentinel byte beyond the limit; never load the whole oversized file.
                const content = new Uint8Array(await file.slice(0, maximumBytes + 1).arrayBuffer());
                resolve({ name: file.name, content });
            } catch (error) {
                reject(error);
            } finally {
                input.remove();
            }
        }, { once: true });
        document.body.append(input);
        try { input.click(); }
        catch (error) { input.remove(); reject(error); }
    });
}

export function saveBackup(name, content) {
    const url = URL.createObjectURL(new Blob([content], { type: "application/json" }));
    const anchor = document.createElement("a");
    try {
        anchor.href = url;
        anchor.download = name;
        document.body.append(anchor);
        anchor.click();
    } finally {
        anchor.remove();
        // Keep the URL alive long enough for the browser to consume the download request.
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
    }
}
