const status = document.getElementById('pwa-status');

function showStatus(message) {
    status.textContent = message;
    status.hidden = false;
}

async function registerOfflineUse() {
    if (!('serviceWorker' in navigator)) {
        showStatus('Offline use is not available in this browser. Keep an internet connection to use RecallDeck.');
        return;
    }

    try {
        const registration = await navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
        const showWaitingUpdate = () => {
            if (registration.waiting && registration.active) {
                showStatus('A RecallDeck update is ready. Save your work, then close all RecallDeck tabs and reopen the app to update. Your open tabs keep their current version.');
            }
        };
        const observeInstalling = () => {
            const worker = registration.installing;
            if (worker) worker.addEventListener('statechange', showWaitingUpdate);
        };
        registration.addEventListener('updatefound', observeInstalling);
        observeInstalling();
        showWaitingUpdate();
    } catch {
        showStatus('RecallDeck could not prepare offline use. Keep a connection and reopen the app to try again. Your saved study data has not been reset.');
    }
}

registerOfflineUse();
