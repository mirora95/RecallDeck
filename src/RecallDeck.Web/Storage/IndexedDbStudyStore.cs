using Microsoft.JSInterop;
using RecallDeck.Core;
using RecallDeck.Core.Storage;

namespace RecallDeck.Web.Storage;

public sealed class IndexedDbStudyStore(IJSRuntime js) : IStudyStore
{
    public async Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync("read", null, null, cancellationToken);
        if (result.Decks is null || result.Cards is null || result.Reviews is null)
            throw new IOException("Browser study data could not be read. Keep this browser's data and retry; do not clear storage.");

        return new StudySnapshot(result.Decks, result.Cards, result.Reviews);
    }

    public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
        => ExecuteAsync("createDeck", deck ?? throw new ArgumentNullException(nameof(deck)), null, cancellationToken);

    public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
        => ExecuteAsync("updateDeck", deck ?? throw new ArgumentNullException(nameof(deck)), null, cancellationToken);

    public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync("deleteDeck", id, null, cancellationToken);

    public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default)
        => ExecuteAsync("createCard", card ?? throw new ArgumentNullException(nameof(card)), null, cancellationToken);

    public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default)
        => ExecuteAsync("updateCard", card ?? throw new ArgumentNullException(nameof(card)), null, cancellationToken);

    public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync("deleteCard", id, null, cancellationToken);

    public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default)
        => ExecuteAsync("applyReview", review ?? throw new ArgumentNullException(nameof(review)), schedule ?? throw new ArgumentNullException(nameof(schedule)), cancellationToken);

    private async Task<StorageResult> ExecuteAsync(string operation, object? value, ReviewSchedule? schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var module = await js.InvokeAsync<IJSInProcessObjectReference>("import", "./study-store.js");
            cancellationToken.ThrowIfCancellationRequested();

            using var pending = module.Invoke<IJSInProcessObjectReference>("begin", operation, value, schedule);
            // WASM in-process cancellation aborts the transaction; canceling only InvokeAsync would leave the write running.
            using var registration = cancellationToken.Register(() => pending.InvokeVoid("cancel"));
            var result = await pending.InvokeAsync<StorageResult>("finish");

            return result.Error switch
            {
                null => result,
                "missing" => throw new KeyNotFoundException("The deck or card was not found."),
                "duplicate" => throw new InvalidOperationException("This identity or review session item already exists."),
                "invalid" => throw new InvalidOperationException("Content edits must preserve the card's deck and schedule."),
                "canceled" => throw new OperationCanceledException("The browser storage operation was canceled.", cancellationToken),
                "quota" => throw new IOException("Browser storage is full. Keep your edits, free space outside this app, then retry. Do not clear this app's data."),
                "unavailable" => throw new IOException("Browser storage is unavailable. Allow site storage in this browser, then retry. Keep any unsaved text."),
                "version" => throw new IOException("This study database needs a newer app version. Reload the updated app; do not clear existing storage."),
                _ => throw new IOException("Browser storage could not complete the operation. Keep your edits and retry; existing data has not been reset.")
            };
        }
        catch (JSException error)
        {
            throw new IOException("Browser storage could not be reached. Keep your edits and retry; do not clear existing storage.", error);
        }
    }

    private sealed class StorageResult
    {
        public string? Error { get; set; }
        public Deck[]? Decks { get; set; }
        public Card[]? Cards { get; set; }
        public ReviewEvent[]? Reviews { get; set; }
    }
}
