using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests;

public class CardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatePersistsValidatedTextAndIsDueImmediately()
    {
        var (store, service, deck, _) = await SetupAsync();

        var created = await service.CreateAsync(deck.Id, "  日本語 😀  ", "  Answer\nline 2  ");
        var snapshot = await store.ReadSnapshotAsync();

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(deck.Id, created.DeckId);
        Assert.Equal("日本語 😀", created.Front);
        Assert.Equal("Answer\nline 2", created.Back);
        Assert.Equal(new ReviewSchedule(0, Now), created.Schedule);
        Assert.Equal(created, snapshot.Cards.Single(card => card.Id == created.Id));
        Assert.Empty(snapshot.Reviews);
    }

    [Fact]
    public async Task EditPreservesIdentityScheduleAndHistory()
    {
        var (store, service, _, card) = await SetupAsync();
        await AddReviewAsync(store, card);
        var before = await store.ReadSnapshotAsync();
        var reviewed = before.Cards.Single(existing => existing.Id == card.Id);

        var edited = await service.EditAsync(card.Id, "  New front  ", "  New back  ");
        var after = await store.ReadSnapshotAsync();

        Assert.Equal(card.Id, edited.Id);
        Assert.Equal(card.DeckId, edited.DeckId);
        Assert.Equal(reviewed.Schedule, edited.Schedule);
        Assert.Equal("New front", edited.Front);
        Assert.Equal("New back", edited.Back);
        Assert.Equal(edited, after.Cards.Single(existing => existing.Id == card.Id));
        Assert.Equal(before.Decks, after.Decks);
        Assert.Equal(before.Reviews, after.Reviews);
        Assert.Equal("Original front", card.Front);
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheCardAndItsHistory()
    {
        var (store, service, deck, card) = await SetupAsync();
        var sibling = await service.CreateAsync(deck.Id, "Sibling", "Answer");
        var otherDeck = new Deck(Guid.NewGuid(), "Other deck");
        await store.CreateDeckAsync(otherDeck);
        var other = await service.CreateAsync(otherDeck.Id, "Other", "Answer");
        await AddReviewAsync(store, card);
        await AddReviewAsync(store, sibling);
        await AddReviewAsync(store, other);
        var before = await store.ReadSnapshotAsync();

        await service.DeleteAsync(card.Id);
        var after = await store.ReadSnapshotAsync();

        Assert.Equal(before.Decks, after.Decks);
        Assert.Equal(before.Cards.Where(existing => existing.Id != card.Id), after.Cards);
        Assert.Equal(before.Reviews.Where(review => review.CardId != card.Id), after.Reviews);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public async Task MissingTextHasFieldFeedbackAndDoesNotWrite(string? text)
    {
        var (store, service, deck, card) = await SetupAsync();
        var before = await store.ReadSnapshotAsync();

        Assert.Equal("front", (await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(deck.Id, text!, "Back"))).ParamName);
        Assert.Equal("back", (await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(deck.Id, "Front", text!))).ParamName);
        Assert.Equal("front", (await Assert.ThrowsAnyAsync<ArgumentException>(() => service.EditAsync(card.Id, text!, "Back"))).ParamName);
        Assert.Equal("back", (await Assert.ThrowsAnyAsync<ArgumentException>(() => service.EditAsync(card.Id, "Front", text!))).ParamName);

        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Fact]
    public async Task TextLimitIsEnforcedForCreateAndEdit()
    {
        var (store, service, deck, card) = await SetupAsync();
        var before = await store.ReadSnapshotAsync();
        var overlong = new string('x', 4001);

        Assert.Equal("front", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(deck.Id, overlong, "Back"))).ParamName);
        Assert.Equal("back", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(deck.Id, "Front", overlong))).ParamName);
        Assert.Equal("front", (await Assert.ThrowsAsync<ArgumentException>(() => service.EditAsync(card.Id, overlong, "Back"))).ParamName);
        Assert.Equal("back", (await Assert.ThrowsAsync<ArgumentException>(() => service.EditAsync(card.Id, "Front", overlong))).ParamName);

        AssertUnchanged(before, await store.ReadSnapshotAsync());

        var boundary = string.Concat(Enumerable.Repeat("😀", 2000));
        var created = await service.CreateAsync(deck.Id, boundary, boundary);
        var edited = await service.EditAsync(card.Id, boundary, boundary);

        Assert.Equal(boundary, created.Front);
        Assert.Equal(boundary, created.Back);
        Assert.Equal(boundary, edited.Front);
        Assert.Equal(boundary, edited.Back);
    }

    [Fact]
    public async Task UnknownDeckAndCardIdsDoNotChangeOtherRecords()
    {
        var (store, service, _, card) = await SetupAsync();
        await AddReviewAsync(store, card);
        var before = await store.ReadSnapshotAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(Guid.NewGuid(), "Front", "Back"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.EditAsync(Guid.NewGuid(), "Front", "Back"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(Guid.NewGuid()));
        Assert.Equal("deckId", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.Empty, "Front", "Back"))).ParamName);

        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("delete")]
    public async Task FailedCommitPropagatesAndPreservesAllRecords(string operation)
    {
        var (store, service, deck, card) = await SetupAsync();
        await AddReviewAsync(store, card);
        var before = await store.ReadSnapshotAsync();
        store.BeforeNextCommit = () => throw new IOException("Injected commit failure");

        await Assert.ThrowsAsync<IOException>(() => OperateAsync(service, operation, deck.Id, card.Id));

        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("edit", false)]
    [InlineData("delete", false)]
    [InlineData("create", true)]
    [InlineData("edit", true)]
    [InlineData("delete", true)]
    public async Task CancellationBeforeOrDuringWritePreservesAllRecords(string operation, bool duringWrite)
    {
        var (store, service, deck, card) = await SetupAsync();
        await AddReviewAsync(store, card);
        var before = await store.ReadSnapshotAsync();
        using var cancellation = new CancellationTokenSource();
        if (duringWrite)
            store.BeforeNextCommit = cancellation.Cancel;
        else
            cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OperateAsync(service, operation, deck.Id, card.Id, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    private static async Task<(InMemoryStudyStore Store, CardService Service, Deck Deck, Card Card)> SetupAsync()
    {
        var store = new InMemoryStudyStore();
        var service = new CardService(store, new ReviewScheduler(new FixedClock()));
        var deck = new Deck(Guid.NewGuid(), "Original deck");
        await store.CreateDeckAsync(deck);
        var card = await service.CreateAsync(deck.Id, "Original front", "Original back");
        return (store, service, deck, card);
    }

    private static Task AddReviewAsync(IStudyStore store, Card card)
        => store.ApplyReviewAsync(
            new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), ReviewRating.Remembered, Now),
            new ReviewSchedule(3, Now.AddDays(7)));

    private static Task OperateAsync(CardService service, string operation, Guid deckId, Guid cardId, CancellationToken cancellationToken = default)
        => operation switch
        {
            "create" => service.CreateAsync(deckId, "New", "Answer", cancellationToken),
            "edit" => service.EditAsync(cardId, "Changed", "Answer", cancellationToken),
            "delete" => service.DeleteAsync(cardId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static void AssertUnchanged(StudySnapshot expected, StudySnapshot actual)
    {
        Assert.Equal(expected.Decks, actual.Decks);
        Assert.Equal(expected.Cards, actual.Cards);
        Assert.Equal(expected.Reviews, actual.Reviews);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
