using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests;

public class ReviewQueueServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeckId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    [Fact]
    public async Task CaptureSelectsOnlyCardsDueAtOrBeforeClockInstant()
    {
        var past = CreateCard(1, Now.AddTicks(-1));
        var equal = CreateCard(2, Now);
        var future = CreateCard(3, Now.AddTicks(1));
        var store = await SeedAsync(future, equal, past);

        var queue = await new ReviewQueueService(store, new FixedClock()).CaptureAsync();

        Assert.Equal(new[] { past, equal }, queue);
    }

    [Fact]
    public async Task CaptureOrdersByDueInstantThenStableIdWithoutChangingStoreOrder()
    {
        var first = CreateCard(4, Now.AddDays(-2));
        var second = CreateCard(2, Now.AddDays(-1));
        var third = CreateCard(3, Now.AddDays(-1).ToOffset(TimeSpan.FromHours(5)));
        var fourth = CreateCard(1, Now);
        var insertionOrder = new[] { third, fourth, second, first };
        var store = await SeedAsync(insertionOrder);
        var service = new ReviewQueueService(store, new FixedClock());

        var queue = await service.CaptureAsync();
        var repeated = await service.CaptureAsync();

        Assert.Equal(new[] { first, second, third, fourth }, queue);
        Assert.Equal(queue, repeated);
        Assert.Equal(insertionOrder, (await store.ReadSnapshotAsync()).Cards);
    }

    [Fact]
    public async Task CaptureSupportsAllDecksOrOneExistingDeck()
    {
        var otherDeckId = Guid.NewGuid();
        var first = CreateCard(1, Now.AddDays(-1));
        var other = CreateCard(2, Now, otherDeckId);
        var future = CreateCard(3, Now.AddTicks(1));
        var store = await SeedAsync(other, future, first);
        var service = new ReviewQueueService(store, new FixedClock());

        Assert.Equal(new[] { first, other }, await service.CaptureAsync());
        Assert.Equal(new[] { first }, await service.CaptureAsync(DeckId));
        Assert.Equal(new[] { other }, await service.CaptureAsync(otherDeckId));
    }

    [Fact]
    public async Task CaptureReturnsEmptyForEmptyStoreOrExistingEmptyDeck()
    {
        var store = new InMemoryStudyStore();
        var service = new ReviewQueueService(store, new FixedClock());

        Assert.Empty(await service.CaptureAsync());

        await store.CreateDeckAsync(new Deck(DeckId, "Empty"));

        Assert.Empty(await service.CaptureAsync(DeckId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureRejectsUnknownOrEmptyDeckId(bool emptyId)
    {
        var store = await SeedAsync(CreateCard(1, Now));
        var service = new ReviewQueueService(store, new FixedClock());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CaptureAsync(emptyId ? Guid.Empty : Guid.NewGuid()));
    }

    [Fact]
    public async Task CapturedQueueRetainsMembershipOrderAndContentAfterStoreChanges()
    {
        var first = CreateCard(1, Now.AddDays(-1));
        var second = CreateCard(2, Now);
        var store = await SeedAsync(second, first);
        var service = new ReviewQueueService(store, new FixedClock());
        var captured = await service.CaptureAsync();
        var edited = new Card(second.Id, second.DeckId, "Edited front", "Edited back", second.Schedule);
        var added = CreateCard(3, Now.AddDays(-2));

        await store.CreateCardAsync(added);
        await store.UpdateCardAsync(edited);
        await store.DeleteCardAsync(first.Id);

        Assert.Equal(new[] { first, second }, captured);
        Assert.Equal("Front 2", captured[1].Front);
        Assert.Equal("Back 2", captured[1].Back);
        Assert.Equal(new[] { added, edited }, await service.CaptureAsync());
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<Card>>(captured).Clear());
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<Card>>(captured)[0] = added);
        Assert.Equal(new[] { first, second }, captured);
    }

    [Fact]
    public async Task CaptureReadsOneSnapshotThenOneClockInstant()
    {
        var past = CreateCard(1, Now.AddTicks(-1));
        var equal = CreateCard(2, Now);
        var future = CreateCard(3, Now.AddTicks(1));
        var read = new TaskCompletionSource<StudySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FixedClock();
        using var cancellation = new CancellationTokenSource();
        var store = new ReadOnlyStudyStore(token =>
        {
            Assert.Equal(cancellation.Token, token);
            return read.Task;
        });
        var service = new ReviewQueueService(store, clock);

        var pending = service.CaptureAsync(cancellationToken: cancellation.Token);

        Assert.Equal(1, store.Reads);
        Assert.Equal(0, clock.Reads);
        read.SetResult(new StudySnapshot([new Deck(DeckId, "Deck")], [future, equal, past], []));
        Assert.Equal(new[] { past, equal }, await pending);
        Assert.Equal(1, store.Reads);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public async Task PrecanceledCaptureDoesNotReadStorageOrClock()
    {
        var store = new ReadOnlyStudyStore(_ => throw new InvalidOperationException("Should not read"));
        var clock = new FixedClock();
        var service = new ReviewQueueService(store, clock);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureAsync(cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public async Task StorageReadFailurePropagatesWithoutReadingClock()
    {
        var failure = new IOException("Injected read failure");
        var store = new ReadOnlyStudyStore(_ => Task.FromException<StudySnapshot>(failure));
        var clock = new FixedClock();

        var error = await Assert.ThrowsAsync<IOException>(() => new ReviewQueueService(store, clock).CaptureAsync());

        Assert.Same(failure, error);
        Assert.Equal(1, store.Reads);
        Assert.Equal(0, clock.Reads);
    }

    [Fact]
    public void ConstructorRequiresStoreAndClock()
    {
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new ReviewQueueService(null!, new FixedClock())).ParamName);
        Assert.Equal("clock", Assert.Throws<ArgumentNullException>(() => new ReviewQueueService(new InMemoryStudyStore(), null!)).ParamName);
    }

    private static Card CreateCard(int id, DateTimeOffset dueAt, Guid? deckId = null)
        => new(Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), deckId ?? DeckId, $"Front {id}", $"Back {id}", new ReviewSchedule(0, dueAt));

    private static async Task<InMemoryStudyStore> SeedAsync(params Card[] cards)
    {
        var store = new InMemoryStudyStore();
        foreach (var deckId in cards.Select(card => card.DeckId).Distinct())
            await store.CreateDeckAsync(new Deck(deckId, "Deck"));
        foreach (var card in cards)
            await store.CreateCardAsync(card);
        return store;
    }

    private sealed class FixedClock : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return Now;
        }
    }

    private sealed class ReadOnlyStudyStore(Func<CancellationToken, Task<StudySnapshot>> read) : IStudyStore
    {
        public int Reads { get; private set; }

        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return read(cancellationToken);
        }

        public Task ReplaceAllAsync(StudySnapshot snapshot, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
