using RecallDeck.Core.Storage;

namespace RecallDeck.Core.Tests.Storage;

public abstract class StudyStoreContractTests
{
    protected abstract StoreHarness CreateStore();

    protected sealed record StoreHarness(IStudyStore Store, Action<Action> BeforeNextCommit);

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmptyStoreReturnsEmptySnapshot()
    {
        var snapshot = await CreateStore().Store.ReadSnapshotAsync();

        Assert.Empty(snapshot.Decks);
        Assert.Empty(snapshot.Cards);
        Assert.Empty(snapshot.Reviews);
    }

    [Fact]
    public async Task CreateAndUpdateKeepIdentitiesAndPreviousSnapshotsStable()
    {
        var store = CreateStore().Store;
        var (deck, card) = await SeedAsync(store);
        var before = await store.ReadSnapshotAsync();

        await store.UpdateDeckAsync(new Deck(deck.Id, "Renamed", deck.Description));
        await store.UpdateCardAsync(new Card(card.Id, deck.Id, "Edited", card.Back, card.Schedule));
        var after = await store.ReadSnapshotAsync();

        Assert.Equal(deck, Assert.Single(before.Decks));
        Assert.Equal(card, Assert.Single(before.Cards));
        Assert.Equal("Renamed", Assert.Single(after.Decks).Title);
        Assert.Equal("Edited", Assert.Single(after.Cards).Front);
        Assert.Equal(card.Id, after.Cards[0].Id);
        Assert.Equal(card.Schedule, after.Cards[0].Schedule);
    }

    [Fact]
    public async Task MissingDuplicateAndOrphanWritesFailWithoutChangingState()
    {
        var store = CreateStore().Store;
        var (deck, card) = await SeedAsync(store);
        var before = await store.ReadSnapshotAsync();
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateDeckAsync(deck));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateCardAsync(card));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateDeckAsync(new Deck(missing, "Missing")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteDeckAsync(missing));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.CreateCardAsync(new Card(Guid.NewGuid(), missing, "front", "back", card.Schedule)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateCardAsync(new Card(missing, deck.Id, "front", "back", card.Schedule)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.DeleteCardAsync(missing));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ApplyReviewAsync(NewReview(missing), new ReviewSchedule(1, Now)));

        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Fact]
    public async Task ContentUpdateCannotMoveCardOrChangeScheduleWithoutReview()
    {
        var store = CreateStore().Store;
        var (deck, card) = await SeedAsync(store);
        var other = new Deck(Guid.NewGuid(), "Other");
        await store.CreateDeckAsync(other);
        var before = await store.ReadSnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateCardAsync(new Card(card.Id, other.Id, card.Front, card.Back, card.Schedule)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateCardAsync(new Card(card.Id, deck.Id, card.Front, card.Back, new ReviewSchedule(1, Now.AddDays(1)))));

        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task ReviewChangesScheduleAndAppendsOneEventTogether(ReviewRating rating)
    {
        var store = CreateStore().Store;
        var (deck, card) = await SeedAsync(store, level: 2);
        var before = await store.ReadSnapshotAsync();
        var review = NewReview(card.Id, rating);
        var schedule = ExpectedReviewSchedule(rating);

        await store.ApplyReviewAsync(review, schedule);
        var snapshot = await store.ReadSnapshotAsync();

        AssertReviewApplied(deck, card, review, schedule, snapshot);
        Assert.Equal(card, Assert.Single(before.Cards));
        Assert.Empty(before.Reviews);
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task DuplicateReviewIdentityOrSessionItemDoesNotRateTwice(ReviewRating rating)
    {
        var store = CreateStore().Store;
        var (_, card) = await SeedAsync(store, level: 2);
        var review = NewReview(card.Id, rating);
        await store.ApplyReviewAsync(review, ExpectedReviewSchedule(rating));
        var before = await store.ReadSnapshotAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyReviewAsync(new ReviewEvent(review.Id, card.Id, Guid.NewGuid(), rating, Now), card.Schedule));
        AssertUnchanged(before, await store.ReadSnapshotAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyReviewAsync(new ReviewEvent(Guid.NewGuid(), card.Id, review.SessionItemId, rating, Now), card.Schedule));
        AssertUnchanged(before, await store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData("create-deck")]
    [InlineData("update-deck")]
    [InlineData("delete-deck")]
    [InlineData("create-card")]
    [InlineData("update-card")]
    [InlineData("delete-card")]
    [InlineData("apply-review")]
    public async Task FailedWritesPropagateAndRollBackEveryCollection(string operation)
    {
        var harness = CreateStore();
        var (deck, card) = await SeedAsync(harness.Store);
        var before = await harness.Store.ReadSnapshotAsync();
        harness.BeforeNextCommit(() => throw new IOException("Injected commit failure"));

        await Assert.ThrowsAsync<IOException>(() => WriteAsync(harness.Store, operation, deck, card));

        AssertUnchanged(before, await harness.Store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData("create-deck")]
    [InlineData("update-deck")]
    [InlineData("delete-deck")]
    [InlineData("create-card")]
    [InlineData("update-card")]
    [InlineData("delete-card")]
    [InlineData("apply-review")]
    public async Task CanceledWritesPropagateWithoutChangingState(string operation)
    {
        var harness = CreateStore();
        var (deck, card) = await SeedAsync(harness.Store);
        var before = await harness.Store.ReadSnapshotAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WriteAsync(harness.Store, operation, deck, card, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Store.ReadSnapshotAsync(cancellation.Token));

        AssertUnchanged(before, await harness.Store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task CancellationAtCommitDoesNotPublishHalfAReviewAndRetrySucceeds(ReviewRating rating)
    {
        var harness = CreateStore();
        var (deck, card) = await SeedAsync(harness.Store, level: 2);
        var before = await harness.Store.ReadSnapshotAsync();
        var review = NewReview(card.Id, rating);
        var schedule = ExpectedReviewSchedule(rating);
        using var cancellation = new CancellationTokenSource();
        harness.BeforeNextCommit(cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Store.ApplyReviewAsync(review, schedule, cancellation.Token));
        AssertUnchanged(before, await harness.Store.ReadSnapshotAsync());

        await harness.Store.ApplyReviewAsync(review, schedule);
        AssertReviewApplied(deck, card, review, schedule, await harness.Store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task FailedReviewCanRetryTheSameEventWithoutPartialHistory(ReviewRating rating)
    {
        var harness = CreateStore();
        var (deck, card) = await SeedAsync(harness.Store, level: 2);
        var before = await harness.Store.ReadSnapshotAsync();
        var review = NewReview(card.Id, rating);
        var schedule = ExpectedReviewSchedule(rating);
        harness.BeforeNextCommit(() => throw new IOException("Injected review failure"));

        await Assert.ThrowsAsync<IOException>(() => harness.Store.ApplyReviewAsync(review, schedule));
        AssertUnchanged(before, await harness.Store.ReadSnapshotAsync());

        await harness.Store.ApplyReviewAsync(review, schedule);
        AssertReviewApplied(deck, card, review, schedule, await harness.Store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task DeletedCardCannotBeReviewedOrChangeRemainingData(ReviewRating rating)
    {
        var store = CreateStore().Store;
        var (deck, card) = await SeedAsync(store, level: 2);
        var remaining = new Card(Guid.NewGuid(), deck.Id, "Other question", "Other answer", card.Schedule);
        await store.CreateCardAsync(remaining);
        await store.ApplyReviewAsync(NewReview(remaining.Id), ExpectedReviewSchedule(ReviewRating.Remembered));
        await store.ApplyReviewAsync(NewReview(card.Id), ExpectedReviewSchedule(ReviewRating.Remembered));
        await store.DeleteCardAsync(card.Id);
        var before = await store.ReadSnapshotAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ApplyReviewAsync(NewReview(card.Id, rating), ExpectedReviewSchedule(rating)));

        AssertUnchanged(before, await store.ReadSnapshotAsync());
        Assert.Equal(remaining.Id, Assert.Single(before.Cards).Id);
        Assert.Equal(remaining.Id, Assert.Single(before.Reviews).CardId);
    }

    [Fact]
    public async Task DeletingCardOrDeckCascadesHistoryButPreservesOtherDecks()
    {
        var store = CreateStore().Store;
        var (first, card) = await SeedAsync(store);
        await store.ApplyReviewAsync(NewReview(card.Id), new ReviewSchedule(1, Now));
        var second = new Deck(Guid.NewGuid(), "Unrelated");
        var secondCard = new Card(Guid.NewGuid(), second.Id, "other", "answer", new ReviewSchedule(0, Now));
        await store.CreateDeckAsync(second);
        await store.CreateCardAsync(secondCard);

        await store.DeleteCardAsync(card.Id);
        var afterCardDelete = await store.ReadSnapshotAsync();
        Assert.Empty(afterCardDelete.Reviews);
        Assert.Equal(secondCard, Assert.Single(afterCardDelete.Cards));
        Assert.Equal(2, afterCardDelete.Decks.Count);

        await store.CreateCardAsync(card);
        await store.ApplyReviewAsync(NewReview(card.Id), new ReviewSchedule(1, Now));
        await store.DeleteDeckAsync(first.Id);
        var afterDeckDelete = await store.ReadSnapshotAsync();
        Assert.Equal(second, Assert.Single(afterDeckDelete.Decks));
        Assert.Equal(secondCard, Assert.Single(afterDeckDelete.Cards));
        Assert.Empty(afterDeckDelete.Reviews);
    }

    [Theory]
    [InlineData("delete-card")]
    [InlineData("delete-deck")]
    public async Task FailedCascadeLeavesCardAndHistoryIntact(string operation)
    {
        var harness = CreateStore();
        var (deck, card) = await SeedAsync(harness.Store);
        await harness.Store.ApplyReviewAsync(NewReview(card.Id), new ReviewSchedule(1, Now));
        var before = await harness.Store.ReadSnapshotAsync();
        harness.BeforeNextCommit(() => throw new IOException("Injected cascade failure"));

        await Assert.ThrowsAsync<IOException>(() => WriteAsync(harness.Store, operation, deck, card));

        AssertUnchanged(before, await harness.Store.ReadSnapshotAsync());
    }

    private static async Task<(Deck Deck, Card Card)> SeedAsync(IStudyStore store, int level = 0)
    {
        var deck = new Deck(Guid.NewGuid(), "日本語 😀", "Description");
        var card = new Card(Guid.NewGuid(), deck.Id, "question\nsecond line", "answer", new ReviewSchedule(level, Now));
        await store.CreateDeckAsync(deck);
        await store.CreateCardAsync(card);
        return (deck, card);
    }

    private static ReviewEvent NewReview(Guid cardId, ReviewRating rating = ReviewRating.Remembered)
        => new(Guid.NewGuid(), cardId, Guid.NewGuid(), rating, Now);

    private static ReviewSchedule ExpectedReviewSchedule(ReviewRating rating)
        => rating == ReviewRating.Again
            ? new ReviewSchedule(0, new DateTimeOffset(2026, 11, 1, 14, 10, 0, TimeSpan.Zero))
            : new ReviewSchedule(3, new DateTimeOffset(2026, 11, 8, 14, 0, 0, TimeSpan.Zero));

    private static void AssertReviewApplied(Deck deck, Card card, ReviewEvent review, ReviewSchedule schedule, StudySnapshot snapshot)
    {
        Assert.Equal(deck, Assert.Single(snapshot.Decks));
        Assert.Equal(new Card(card.Id, card.DeckId, card.Front, card.Back, schedule), Assert.Single(snapshot.Cards));
        Assert.Equal(TimeSpan.Zero, snapshot.Cards[0].Schedule.DueAt.Offset);
        Assert.Equal(review, Assert.Single(snapshot.Reviews));
    }

    private static Task WriteAsync(IStudyStore store, string operation, Deck deck, Card card, CancellationToken cancellationToken = default)
        => operation switch
        {
            "create-deck" => store.CreateDeckAsync(new Deck(Guid.NewGuid(), "New"), cancellationToken),
            "update-deck" => store.UpdateDeckAsync(new Deck(deck.Id, "Renamed"), cancellationToken),
            "delete-deck" => store.DeleteDeckAsync(deck.Id, cancellationToken),
            "create-card" => store.CreateCardAsync(new Card(Guid.NewGuid(), deck.Id, "new", "answer", card.Schedule), cancellationToken),
            "update-card" => store.UpdateCardAsync(new Card(card.Id, deck.Id, "edited", "answer", card.Schedule), cancellationToken),
            "delete-card" => store.DeleteCardAsync(card.Id, cancellationToken),
            "apply-review" => store.ApplyReviewAsync(NewReview(card.Id), new ReviewSchedule(1, Now.AddDays(1)), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static void AssertUnchanged(StudySnapshot before, StudySnapshot after)
    {
        Assert.Equal(before.Decks.OrderBy(deck => deck.Id), after.Decks.OrderBy(deck => deck.Id));
        Assert.Equal(before.Cards.OrderBy(card => card.Id), after.Cards.OrderBy(card => card.Id));
        Assert.Equal(before.Reviews.OrderBy(review => review.Id), after.Reviews.OrderBy(review => review.Id));
    }
}

public sealed class InMemoryStudyStoreTests : StudyStoreContractTests
{
    protected override StoreHarness CreateStore()
    {
        var store = new InMemoryStudyStore();
        return new StoreHarness(store, action => store.BeforeNextCommit = action);
    }
}
