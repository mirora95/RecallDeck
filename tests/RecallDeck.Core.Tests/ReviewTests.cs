using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;
using RecallDeck.UI;
using RecallDeck.UI.Pages;

namespace RecallDeck.Core.Tests;

public sealed class ReviewTests : BunitContext
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-27T06:00:00Z");
    private readonly ControlledStore store = new();
    private readonly FixedClock clock = new();

    public ReviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<RecallDeck.UI.AppNavigation>();
        Services.AddScoped<RecallDeck.UI.AppLifecycle>();
        Services.AddScoped<StudyOverviewService>();
        Services.AddSingleton<IStudyStore>(store);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddScoped<ReviewQueueService>();
        Services.AddScoped<ReviewScheduler>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeDoesNotInsertNewlyDueCardsIntoActiveQueue(bool reveal)
    {
        var cards = await SeedAsync();
        await store.Inner.CreateCardAsync(new Card(Guid.NewGuid(), cards[0].DeckId, "Future question", "Future answer",
            new ReviewSchedule(0, Now.AddMinutes(1))));
        var page = Render<Review>();
        if (reveal) page.Find("#reveal-answer").Click();
        var markup = page.Markup;
        clock.Instant = Now.AddMinutes(2);

        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();

        Assert.Equal(markup, page.Markup);
        Assert.DoesNotContain("Future question", page.Markup);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ResumeDuringRatingDoesNotDuplicateOrResetPendingWrite()
    {
        await SeedAsync();
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var rating = page.Find("#rate-again").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));

        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();

        Assert.Single(page.FindAll(".review-answer"));
        Assert.Equal(1, store.Writes);
        store.WriteGate.SetResult();
        await rating;
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Contains("Session complete", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeRefreshesEmptyOrCompletedQueueWhenAgainBecomesDue(bool complete)
    {
        await SeedAsync();
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        await page.Find("#rate-again").ClickAsync(new());
        if (!complete)
        {
            page.Dispose();
            page = Render<Review>();
            Assert.Contains("No cards due", page.Markup);
        }
        clock.Instant = Now.AddMinutes(10);

        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();

        Assert.Single(page.FindAll("#reveal-answer"));
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public async Task ResumePreservesActiveRevealedAndFailedRatingWithoutWriting()
    {
        await SeedAsync(2);
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        store.FailWrite = true;
        await page.Find("#rate-again").ClickAsync(new());
        var markup = page.Markup;
        var writes = store.Writes;
        clock.Instant = Now.AddDays(1);

        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();
        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();

        Assert.Equal(markup, page.Markup);
        Assert.Equal(writes, store.Writes);
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public async Task DisposedReviewDoesNotRefreshAndRepeatedResumeCoalescesLoading()
    {
        var page = Render<Review>();
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = Services.GetRequiredService<AppLifecycle>();
        var first = lifecycle.ResumeAsync();
        var second = lifecycle.ResumeAsync();
        Assert.Same(first, second);
        page.Dispose();
        store.ReadGate.SetResult(new StudySnapshot([], [], []));

        await first;
        await second;
        await lifecycle.ResumeAsync();
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public async Task QuestionOnlyUntilExplicitRevealAndNextItemResets()
    {
        var cards = await SeedAsync(2);
        var page = Render<Review>();

        Assert.Equal(cards[0].Front, page.Find(".review-question .card-text").TextContent);
        Assert.Empty(page.FindAll(".review-answer, #rate-again, #rate-remembered"));
        Assert.Equal("button", page.Find("#reveal-answer").GetAttribute("type"));

        page.Find("#reveal-answer").Click();

        Assert.Equal(cards[0].Back, page.Find(".review-answer .card-text").TextContent);
        Assert.Equal("button", page.Find("#rate-remembered").GetAttribute("type"));
        await page.Find("#rate-remembered").ClickAsync(new());

        Assert.Equal(cards[1].Front, page.Find(".review-question .card-text").TextContent);
        Assert.Empty(page.FindAll(".review-answer, #rate-again, #rate-remembered"));
        Assert.Contains("2 of 2", page.Find("#review-progress").TextContent);
        var review = Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Equal(ReviewRating.Remembered, review.Rating);
        Assert.Equal(new ReviewSchedule(1, Now.AddDays(1)), (await store.Inner.ReadSnapshotAsync()).Cards.Single(c => c.Id == review.CardId).Schedule);
    }

    [Fact]
    public async Task LongUnicodeAndMarkupRemainLiteralOnBothSides()
    {
        var cards = await SeedAsync();
        var frontStart = "<script>bad()</script>\n日本語 😀";
        var front = frontStart + new string('q', 4000 - frontStart.Length);
        var backStart = "<img src=x onerror=bad()>\n";
        var back = backStart + new string('a', 4000 - backStart.Length);
        await store.Inner.UpdateCardAsync(new Card(cards[0].Id, cards[0].DeckId, front, back, cards[0].Schedule));
        var page = Render<Review>();

        Assert.Equal(front, page.Find(".review-question .card-text").TextContent);
        Assert.DoesNotContain(back, page.Markup);
        page.Find("#reveal-answer").Click();

        Assert.Equal(back, page.Find(".review-answer .card-text").TextContent);
        Assert.Empty(page.FindAll("script, img"));
    }

    [Fact]
    public async Task CapturedOrderDoesNotInsertNewCardsAndSummaryCountsOnlySuccessfulRatings()
    {
        var cards = await SeedAsync(2);
        var page = Render<Review>();
        await store.Inner.CreateCardAsync(new Card(Guid.NewGuid(), cards[0].DeckId, "New later", "Back", new ReviewSchedule(0, Now.AddDays(-2))));

        page.Find("#reveal-answer").Click();
        await page.Find("#rate-again").ClickAsync(new());
        page.Find("#reveal-answer").Click();
        await page.Find("#rate-remembered").ClickAsync(new());

        Assert.Contains("Session complete", page.Find("h2").TextContent);
        Assert.Contains("2 reviewed", page.Find("#review-summary").TextContent);
        Assert.Contains("0 skipped", page.Find("#review-summary").TextContent);
        Assert.DoesNotContain("New later", page.Markup);
        Assert.Empty(page.FindAll("#reveal-answer"));
        var snapshot = await store.Inner.ReadSnapshotAsync();
        Assert.Equal(2, snapshot.Reviews.Count);
        Assert.Equal(new ReviewSchedule(0, Now.AddMinutes(10)), snapshot.Cards.Single(c => c.Id == cards[0].Id).Schedule);
    }

    [Fact]
    public async Task ReadFailureAndEmptyQueueHaveSafeRetryStates()
    {
        store.FailRead = true;
        var page = Render<Review>();

        Assert.Contains("could not load", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private", page.Markup);
        store.FailRead = false;
        await page.Find("#retry-load").ClickAsync(new());

        Assert.Contains("No cards due", page.Find("h2").TextContent);
        Assert.Empty(page.FindAll("#reveal-answer, #rate-again, #rate-remembered"));
    }

    [Fact]
    public async Task LoadingQueueHasNoReviewActions()
    {
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Render<Review>();

        Assert.Contains("Loading", page.Find("[role='status']").TextContent);
        Assert.Empty(page.FindAll("#reveal-answer, #rate-again"));
        store.ReadGate.SetResult(new StudySnapshot([], [], []));
        page.WaitForAssertion(() => Assert.Contains("No cards due", page.Find("h2").TextContent));
    }

    [Fact]
    public async Task PendingRatingRejectsRapidSecondActivationAndUsesLatestSchedule()
    {
        var cards = await SeedAsync();
        var page = Render<Review>();
        await store.Inner.ApplyReviewAsync(new ReviewEvent(Guid.NewGuid(), cards[0].Id, Guid.NewGuid(), ReviewRating.Remembered, Now.AddDays(-7)), new ReviewSchedule(3, Now));
        await store.Inner.UpdateCardAsync(new Card(cards[0].Id, cards[0].DeckId, "Edited", "Back", new ReviewSchedule(3, Now)));
        page.Find("#reveal-answer").Click();
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = page.Find("#rate-remembered").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));
        await page.Find("#rate-again").ClickAsync(new());

        Assert.All(page.FindAll(".review-actions button"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.Equal(1, store.Writes);
        store.WriteGate.SetResult();
        await pending;

        var saved = await store.Inner.ReadSnapshotAsync();
        Assert.Equal(2, saved.Reviews.Count);
        Assert.Equal("Edited", Assert.Single(saved.Cards).Front);
        Assert.Equal(new ReviewSchedule(4, Now.AddDays(14)), saved.Cards[0].Schedule);
    }

    [Fact]
    public async Task FailureRetainsAnswerAndRetriesTheSameEventOnce()
    {
        var cards = await SeedAsync();
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        store.FailWrite = true;

        await page.Find("#rate-again").ClickAsync(new());

        Assert.Equal(cards[0].Back, page.Find(".review-answer .card-text").TextContent);
        Assert.Contains("could not save", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private", page.Markup);
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Equal(cards[0], Assert.Single((await store.Inner.ReadSnapshotAsync()).Cards));
        store.FailWrite = false;
        clock.Instant = Now.AddDays(4);
        await page.Find("#retry-review").ClickAsync(new());

        Assert.Equal(2, store.Attempts.Count);
        Assert.Equal(store.Attempts[0], store.Attempts[1]);
        Assert.Equal(Now, store.Attempts[1].ReviewedAt);
        Assert.Equal(new ReviewSchedule(0, Now.AddMinutes(10)), Assert.Single((await store.Inner.ReadSnapshotAsync()).Cards).Schedule);
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Contains("1 reviewed", page.Find("#review-summary").TextContent);
    }

    [Fact]
    public async Task UncertainCompletionReconcilesAlreadyCommittedSessionItem()
    {
        await SeedAsync();
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        store.FailAfterCommit = true;

        await page.Find("#rate-remembered").ClickAsync(new());
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        await page.Find("#retry-review").ClickAsync(new());

        Assert.Equal(1, store.Writes);
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Contains("1 reviewed", page.Find("#review-summary").TextContent);
    }

    [Fact]
    public async Task RemovedCardIsSkippedWithoutClaimingAReview()
    {
        var cards = await SeedAsync(2);
        var page = Render<Review>();
        await store.Inner.DeleteCardAsync(cards[0].Id);
        page.Find("#reveal-answer").Click();

        await page.Find("#rate-again").ClickAsync(new());
        Assert.Contains("no longer available", page.Find("[aria-live='polite']").TextContent);
        Assert.Equal(cards[1].Front, page.Find(".review-question .card-text").TextContent);
        page.Find("#reveal-answer").Click();
        await page.Find("#rate-remembered").ClickAsync(new());

        Assert.Contains("1 reviewed", page.Find("#review-summary").TextContent);
        Assert.Contains("1 skipped", page.Find("#review-summary").TextContent);
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public async Task FailedReadBeforeRatingDoesNotTreatTheCardAsDeleted()
    {
        var cards = await SeedAsync();
        var page = Render<Review>();
        page.Find("#reveal-answer").Click();
        store.FailRead = true;

        await page.Find("#rate-again").ClickAsync(new());

        Assert.Single(page.FindAll(".review-answer"));
        Assert.Empty(page.FindAll("#review-summary"));
        Assert.Equal(0, store.Writes);
        store.FailRead = false;
        await page.Find("#retry-review").ClickAsync(new());
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public async Task DeckFilterDoesNotSilentlyFallBackToAllCards()
    {
        var cards = await SeedAsync();
        var other = new Deck(Guid.NewGuid(), "Other");
        await store.Inner.CreateDeckAsync(other);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/review?deck={other.Id}");
        var page = Render<App>();

        Assert.Contains("No cards due", page.Find("h2").TextContent);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/review?deck={Guid.NewGuid()}");
        Assert.Contains("Deck unavailable", page.Find("h2").TextContent);
        Assert.Empty(page.FindAll("#reveal-answer"));
        Assert.DoesNotContain(cards[0].Front, page.Markup);
    }

    [Fact]
    public async Task LeavingDuringPreSaveReadDoesNotStartAWriteOrFocusOldPage()
    {
        await SeedAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/review");
        var page = Render<App>();
        page.Find("#reveal-answer").Click();
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = page.Find("#rate-again").ClickAsync(new());
        var snapshot = await store.Inner.ReadSnapshotAsync();

        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        var focusCalls = JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        store.ReadGate.SetResult(snapshot);
        await pending;

        Assert.Equal(0, store.Writes);
        Assert.Equal("Progress", page.Find("h1").TextContent);
        Assert.Equal(focusCalls, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
    }

    [Fact]
    public async Task LeavingDuringWritePreservesCommitWithoutChangingNewPage()
    {
        await SeedAsync();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/review");
        var page = Render<App>();
        page.Find("#reveal-answer").Click();
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = page.Find("#rate-again").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));

        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        var focusCalls = JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        store.WriteGate.SetResult();
        await pending;

        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Equal("Progress", page.Find("h1").TextContent);
        Assert.Equal(focusCalls, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
    }

    [Theory]
    [InlineData(ReviewRating.Again)]
    [InlineData(ReviewRating.Remembered)]
    public async Task ANewSessionDoesNotRepeatSuccessfullyScheduledCards(ReviewRating rating)
    {
        await SeedAsync();
        var first = Render<Review>();
        first.Find("#reveal-answer").Click();
        await first.Find(rating == ReviewRating.Again ? "#rate-again" : "#rate-remembered").ClickAsync(new());

        var second = Render<Review>();

        Assert.Contains("No cards due", second.Find("h2").TextContent);
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Empty(second.FindAll("#reveal-answer, #rate-again, #rate-remembered"));
    }

    [Fact]
    public async Task ChangingDeckDuringRatingReadCannotWriteOrAdvanceTheNewSession()
    {
        var cards = await SeedAsync();
        var other = new Deck(Guid.NewGuid(), "Other");
        await store.Inner.CreateDeckAsync(other);
        var secondCard = new Card(Guid.NewGuid(), other.Id, "Other question", "Other answer", new ReviewSchedule(0, Now));
        await store.Inner.CreateCardAsync(secondCard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/review?deck={cards[0].DeckId}");
        var page = Render<App>();
        page.Find("#reveal-answer").Click();
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead = store.ReadGate;
        var pending = page.Find("#rate-again").ClickAsync(new());
        store.ReadGate = null;

        await page.InvokeAsync(() => navigation.NavigateTo($"/review?deck={other.Id}"));
        oldRead.SetResult(await store.Inner.ReadSnapshotAsync());
        await pending;

        Assert.Equal("Other question", page.Find(".review-question .card-text").TextContent);
        Assert.Empty(page.FindAll(".review-answer"));
        Assert.Equal(0, store.Writes);
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Reviews);
    }
    private async Task<Card[]> SeedAsync(int count = 1)
    {
        var deck = new Deck(Guid.NewGuid(), "Synthetic review");
        await store.Inner.CreateDeckAsync(deck);
        var cards = Enumerable.Range(0, count).Select(i => new Card(Guid.NewGuid(), deck.Id, $"Question {i}", $"Answer {i}", new ReviewSchedule(0, Now.AddMinutes(i - count)))).ToArray();
        foreach (var card in cards) await store.Inner.CreateCardAsync(card);
        return cards;
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => Instant;
    }

    private sealed class ControlledStore : IStudyStore
    {
        public InMemoryStudyStore Inner { get; } = new();
        public TaskCompletionSource<StudySnapshot>? ReadGate { get; set; }
        public TaskCompletionSource? WriteGate { get; set; }
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }
        public bool FailAfterCommit { get; set; }
        public int Writes { get; private set; }
        public List<ReviewEvent> Attempts { get; } = [];
        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken token = default) =>
            FailRead ? Task.FromException<StudySnapshot>(new IOException("private read")) : ReadGate?.Task ?? Inner.ReadSnapshotAsync(token);
        public async Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken token = default)
        {
            Writes++;
            Attempts.Add(review);
            if (WriteGate is not null) await WriteGate.Task;
            if (FailWrite) throw new IOException("private write");
            await Inner.ApplyReviewAsync(review, schedule, token);
            if (FailAfterCommit) throw new IOException("private uncertain commit");
        }
        public Task ReplaceAllAsync(StudySnapshot snapshot, CancellationToken cancellationToken = default) => Inner.ReplaceAllAsync(snapshot, cancellationToken);
        public Task CreateDeckAsync(Deck deck, CancellationToken t = default) => Inner.CreateDeckAsync(deck, t);
        public Task UpdateDeckAsync(Deck deck, CancellationToken t = default) => Inner.UpdateDeckAsync(deck, t);
        public Task DeleteDeckAsync(Guid id, CancellationToken t = default) => Inner.DeleteDeckAsync(id, t);
        public Task CreateCardAsync(Card card, CancellationToken t = default) => Inner.CreateCardAsync(card, t);
        public Task UpdateCardAsync(Card card, CancellationToken t = default) => Inner.UpdateCardAsync(card, t);
        public Task DeleteCardAsync(Guid id, CancellationToken t = default) => Inner.DeleteCardAsync(id, t);
    }
}
