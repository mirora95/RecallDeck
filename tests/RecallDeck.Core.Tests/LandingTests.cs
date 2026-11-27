using Bunit;
using Microsoft.AspNetCore.Components;
using RecallDeck.Core.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.Core.Storage;
using RecallDeck.UI;
using RecallDeck.UI.Pages;
using static RecallDeck.Core.Tests.StudyOverviewServiceTests;

namespace RecallDeck.Core.Tests;

public sealed class LandingTests : BunitContext
{
    private readonly OverviewTestStore store = new(new StudySnapshot([], [], []));
    private readonly OverviewClock clock = new();

    public LandingTests()
    {
        Services.AddSingleton<IStudyStore>(store);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddScoped<StudyOverviewService>();
        Services.AddScoped<AppLifecycle>();
        Services.AddScoped<AppNavigation>();
        Services.AddScoped<ReviewQueueService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmptyHomeExplainsHowToCreateCardsWithoutPretendStatistics()
    {
        var page = Render<Home>();

        Assert.Equal("Make room for what you learn.", page.Find("h1").TextContent);
        Assert.Contains("No cards are scheduled", page.Markup);
        Assert.Contains("Create a deck", page.Markup);
        Assert.Single(page.FindAll("a[href='/decks']"));
        Assert.Empty(page.FindAll("#release-plan"));
    }

    [Fact]
    public void HomeShowsCoherentDueCountsAndRealReviewLinks()
    {
        var deck = new Deck(Id(1), "<script>Deck</script>");
        store.Snapshot = new StudySnapshot([deck], [Card(2, deck.Id, Now), Card(3, deck.Id, Now.AddDays(1))], []);

        var page = Render<Home>();

        Assert.Equal("1 card due now.", page.Find("#due-total").TextContent);
        Assert.Equal("1 due", page.Find(".deck-due-count").TextContent);
        Assert.Single(page.FindAll($"a[href='/review?deck={deck.Id}']"));
        Assert.Single(page.FindAll($"a[href='/decks/{deck.Id}']"));
        Assert.Empty(page.FindAll("script"));
        Assert.Equal(1, store.Reads);
        Assert.Equal(1, clock.Reads);
    }

    [Fact]
    public void FutureScheduleShowsLocalEnglishDateOffsetAcrossUtcDayBoundaryWithoutChangingStorage()
    {
        var deck = new Deck(Id(1), "Deck");
        var due = Now.AddHours(1);
        store.Snapshot = new StudySnapshot([deck], [Card(2, deck.Id, due)], []);

        var page = Render<Home>();

        Assert.Equal("0 cards due now.", page.Find("#due-total").TextContent);
        Assert.Equal("28 Nov 2026, 01:30 (UTC+05:00)", page.Find("#next-review").TextContent);
        Assert.Equal(due.ToString("O"), page.Find("#next-review").GetAttribute("datetime"));
        Assert.Equal(due, store.Snapshot.Cards[0].Schedule.DueAt);
        Assert.Equal(TimeSpan.Zero, store.Snapshot.Cards[0].Schedule.DueAt.Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadingFailureRetryAndResumeNeverShowMisleadingZeroOrPrivateErrors(bool progress)
    {
        store.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = progress ? (IRenderedComponent<Microsoft.AspNetCore.Components.IComponent>)Render<Progress>() : Render<Home>();
        Assert.Contains("Loading", page.Find("[role='status']").TextContent);
        Assert.Empty(page.FindAll("#due-total, #rating-totals"));
        store.Gate.SetException(new IOException("private read details"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[role='alert']")));
        Assert.DoesNotContain("private read details", page.Markup);
        Assert.Empty(page.FindAll("#due-total, #rating-totals"));
        store.Gate = null;
        await page.Find("#retry-overview").ClickAsync(new());
        Assert.Empty(page.FindAll("[role='alert']"));

        store.FailRead = true;
        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();

        Assert.Single(page.FindAll("[role='alert']"));
        Assert.Empty(page.FindAll("#due-total, #rating-totals"));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRefreshAndResumeReadAgainAndCoalesceWhileDisposedCompletionIsIgnored(bool progress)
    {
        var page = progress ? (IRenderedComponent<Microsoft.AspNetCore.Components.IComponent>)Render<Progress>() : Render<Home>();
        var deck = new Deck(Id(1), "Deck");
        var card = Card(2, deck.Id, Now.AddMinutes(10));
        var review = new ReviewEvent(Id(3), card.Id, Id(4), ReviewRating.Again, Now);
        store.Snapshot = new StudySnapshot([deck], [card], [review]);
        await page.Find("#refresh-overview").ClickAsync(new());
        if (progress) Assert.Equal("1", page.Find("#again-total").TextContent);
        else Assert.Single(page.FindAll("#next-review"));
        clock.Instant = Now.AddMinutes(10);
        await Services.GetRequiredService<AppLifecycle>().ResumeAsync();
        if (!progress) Assert.Equal("1 card due now.", page.Find("#due-total").TextContent);
        store.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = Services.GetRequiredService<AppLifecycle>();
        var pending = lifecycle.ResumeAsync();
        var repeated = lifecycle.ResumeAsync();
        Assert.Same(pending, repeated);
        var reads = store.Reads;
        await DisposeComponentsAsync();
        store.Gate.SetResult(store.Snapshot);
        await pending;
        await repeated;
        await lifecycle.ResumeAsync();
        Assert.Equal(reads, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ReturningFromAnActualSavedReviewReloadsHomeCountsAndProgressHistory()
    {
        var saved = new InMemoryStudyStore();
        Services.AddSingleton<IStudyStore>(saved);
        var deck = new Deck(Id(1), "Deck");
        await saved.CreateDeckAsync(deck);
        await saved.CreateCardAsync(Card(2, deck.Id, Now));
        var page = Render<App>();
        Assert.Equal("1 card due now.", page.Find("#due-total").TextContent);
        var navigation = Services.GetRequiredService<NavigationManager>();
        await page.InvokeAsync(() => navigation.NavigateTo("/review"));
        page.Find("#reveal-answer").Click();
        await page.Find("#rate-remembered").ClickAsync(new());
        await page.InvokeAsync(() => navigation.NavigateTo("/"));

        Assert.Equal("0 cards due now.", page.Find("#due-total").TextContent);
        Assert.Single(page.FindAll("#next-review"));
        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        Assert.Equal("1", page.Find("#remembered-total").TextContent);
        Assert.Single(page.FindAll(".history-row"));
        Assert.Single((await saved.ReadSnapshotAsync()).Reviews);
    }

    [Fact]
    public void ProgressShowsActualRatingsPlainQuestionAndValidDeckLinkWithLocalEventTime()
    {
        var deck = new Deck(Id(1), "<b>Deck</b>");
        var card = new Card(Id(2), deck.Id, "<img src=x>Question", "Answer", new ReviewSchedule(0, Now));
        var events = new[] { new ReviewEvent(Id(3), card.Id, Id(5), ReviewRating.Again, Now),
            new ReviewEvent(Id(4), card.Id, Id(6), ReviewRating.Remembered, Now.AddMinutes(-1)) };
        store.Snapshot = new StudySnapshot([deck], [card], events);

        var page = Render<Progress>();

        Assert.Equal("1", page.Find("#again-total").TextContent);
        Assert.Equal("1", page.Find("#remembered-total").TextContent);
        Assert.Equal(2, page.FindAll(".history-row").Count);
        Assert.Equal(card.Front, page.Find(".history-question").TextContent);
        Assert.Equal("Again", page.Find(".history-rating").TextContent);
        Assert.Equal("28 Nov 2026, 00:30 (UTC+05:00)", page.Find(".history-time").TextContent);
        Assert.Single(page.FindAll("#recent-reviews"));
        Assert.Equal(2, page.FindAll($"a[href='/decks/{deck.Id}']").Count);
        Assert.Empty(page.FindAll("img, b"));
        Assert.Contains("Most recent 20 reviews", page.Markup);
        Assert.Contains("Deleting a card or deck removes its review history", page.Markup);
    }

    [Fact]
    public void EmptyAndOrphanProgressHaveHonestTextAndNoBrokenLinks()
    {
        var page = Render<Progress>();
        Assert.Contains("No reviews saved yet", page.Markup);
        Assert.Equal("0", page.Find("#again-total").TextContent);
        page.Dispose();
        store.Snapshot = new StudySnapshot([], [], [new ReviewEvent(Id(1), Id(2), Id(3), ReviewRating.Remembered, Now)]);

        var orphan = Render<Progress>();

        Assert.Contains("Card unavailable", orphan.Markup);
        Assert.Contains("Deck unavailable", orphan.Markup);
        Assert.Empty(orphan.FindAll(".history-deck"));
        Assert.Equal("1", orphan.Find("#remembered-total").TextContent);
    }
}
