using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests;

public sealed class StudyOverviewServiceTests
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T19:30:00Z");

    [Fact]
    public async Task DueSummaryUsesInclusiveBoundaryAndStableDeckOrderFromOneReadAndClockInstant()
    {
        var alpha = new Deck(Id(1), "Alpha");
        var beta = new Deck(Id(2), "Beta");
        var empty = new Deck(Id(3), "Alpha");
        var cards = new[] { Card(11, alpha.Id, Now.AddTicks(-1)), Card(12, alpha.Id, Now),
            Card(13, beta.Id, Now.AddTicks(1)), Card(14, beta.Id, Now.AddDays(2)) };
        var store = new OverviewTestStore(new StudySnapshot([beta, empty, alpha], cards, []));
        var clock = new OverviewClock();

        var summary = await new StudyOverviewService(store, clock).ReadAsync();

        Assert.Equal(1, store.Reads);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(2, summary.DueCount);
        Assert.Equal(4, summary.CardCount);
        Assert.Equal(Now.AddTicks(1), summary.NextDueAt);
        Assert.Equal(new[] { alpha, empty, beta }, summary.Decks.Select(row => row.Deck));
        Assert.Equal(new[] { 2, 0, 0 }, summary.Decks.Select(row => row.DueCount));
        Assert.Equal(cards, store.Snapshot.Cards);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task EmptyAndEntirelyDueSnapshotsHaveNoFutureSchedule()
    {
        var store = new OverviewTestStore(new StudySnapshot([], [], []));
        var service = new StudyOverviewService(store, new OverviewClock());

        var empty = await service.ReadAsync();

        Assert.Empty(empty.Decks);
        Assert.Equal(0, empty.DueCount);
        Assert.Null(empty.NextDueAt);
        var deck = new Deck(Id(1), "Deck");
        store.Snapshot = new StudySnapshot([deck], [Card(2, deck.Id, Now)], []);
        var due = await service.ReadAsync();
        Assert.Equal(1, due.DueCount);
        Assert.Null(due.NextDueAt);
    }

    [Fact]
    public async Task TotalsUseAllRetainedEventsWhileRecentHistoryUsesActualUtcTimeThenId()
    {
        var deck = new Deck(Id(1), "Deck");
        var card = Card(2, deck.Id, Now);
        var events = Enumerable.Range(1, 25).Select(i => new ReviewEvent(Id(i + 100), card.Id, Id(i + 200),
            i % 2 == 0 ? ReviewRating.Again : ReviewRating.Remembered,
            Now.AddMinutes(i / 2).ToOffset(TimeSpan.FromHours(-4)))).Reverse().ToArray();
        var store = new OverviewTestStore(new StudySnapshot([deck], [card], events));

        var summary = await new StudyOverviewService(store, new OverviewClock()).ReadAsync();

        Assert.Equal(12, summary.AgainCount);
        Assert.Equal(13, summary.RememberedCount);
        Assert.Equal(20, summary.RecentReviews.Count);
        Assert.Equal(events.OrderByDescending(e => e.ReviewedAt).ThenBy(e => e.Id).Take(20), summary.RecentReviews.Select(row => row.Event));
        Assert.All(summary.RecentReviews, row => { Assert.Equal(card, row.Card); Assert.Equal(deck, row.Deck); });
        Assert.All(events, e => Assert.Equal(TimeSpan.Zero, e.ReviewedAt.Offset));
        Assert.Throws<NotSupportedException>(() => ((IList<ReviewHistoryItem>)summary.RecentReviews).Clear());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDeletionRemovesLinkedEventsFromTotalsAndRecentHistory(bool deleteDeck)
    {
        var deck = new Deck(Id(1), "Deck");
        var card = Card(2, deck.Id, Now);
        var store = new InMemoryStudyStore();
        await store.CreateDeckAsync(deck);
        await store.CreateCardAsync(card);
        await store.ApplyReviewAsync(new ReviewEvent(Id(3), card.Id, Id(4), ReviewRating.Again, Now), new ReviewSchedule(0, Now.AddMinutes(10)));
        var service = new StudyOverviewService(store, new OverviewClock());
        Assert.Equal(1, (await service.ReadAsync()).AgainCount);

        if (deleteDeck) await store.DeleteDeckAsync(deck.Id);
        else await store.DeleteCardAsync(card.Id);
        var summary = await service.ReadAsync();

        Assert.Equal(0, summary.AgainCount);
        Assert.Equal(0, summary.RememberedCount);
        Assert.Empty(summary.RecentReviews);
    }

    [Fact]
    public async Task OrphanHistoryKeepsActualEventWithoutInventingCardOrDeck()
    {
        var review = new ReviewEvent(Id(1), Id(2), Id(3), ReviewRating.Again, Now);
        var store = new OverviewTestStore(new StudySnapshot([], [], [review]));

        var summary = await new StudyOverviewService(store, new OverviewClock()).ReadAsync();

        Assert.Equal(1, summary.AgainCount);
        var row = Assert.Single(summary.RecentReviews);
        Assert.Equal(review, row.Event);
        Assert.Null(row.Card);
        Assert.Null(row.Deck);
    }

    [Fact]
    public async Task ClockIsCapturedOnlyAfterSnapshotReadAndReadFailureDoesNotBecomeZero()
    {
        var store = new OverviewTestStore(new StudySnapshot([], [], [])) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var clock = new OverviewClock();
        var service = new StudyOverviewService(store, clock);
        var pending = service.ReadAsync();
        Assert.Equal(0, clock.Reads);
        store.Gate.SetResult(store.Snapshot);
        await pending;
        Assert.Equal(1, clock.Reads);
        store.Gate = null;
        store.FailRead = true;

        await Assert.ThrowsAsync<IOException>(() => service.ReadAsync());

        Assert.Equal(1, clock.Reads);
        Assert.Equal(0, store.Writes);
    }

    internal static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    internal static Card Card(int value, Guid deckId, DateTimeOffset due) => new(Id(value), deckId, $"Question {value}", "Answer", new ReviewSchedule(0, due));
}

internal sealed class OverviewClock : TimeProvider
{
    public DateTimeOffset Instant { get; set; } = StudyOverviewServiceTests.Now;
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow() { Reads++; return Instant; }
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test local", TimeSpan.FromHours(5), "Test local", "Test local");
}

internal sealed class OverviewTestStore(StudySnapshot snapshot) : IStudyStore
{
    public StudySnapshot Snapshot { get; set; } = snapshot;
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public bool FailRead { get; set; }
    public TaskCompletionSource<StudySnapshot>? Gate { get; set; }
    public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        cancellationToken.ThrowIfCancellationRequested();
        return FailRead ? Task.FromException<StudySnapshot>(new IOException("private read details")) : Gate?.Task ?? Task.FromResult(Snapshot);
    }
    private Task Write() { Writes++; throw new NotSupportedException("Overview must never write"); }
    public Task ReplaceAllAsync(StudySnapshot snapshot, CancellationToken cancellationToken = default) => Write();
    public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => Write();
    public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => Write();
    public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default) => Write();
    public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default) => Write();
    public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default) => Write();
    public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default) => Write();
    public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default) => Write();
}
