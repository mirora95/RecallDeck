using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;
using RecallDeck.UI;
using RecallDeck.UI.Pages;

namespace RecallDeck.Core.Tests;

public class DeckDetailTests : BunitContext
{
    private readonly ControlledStore store = new();
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    public DeckDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IStudyStore>(store);
        Services.AddSingleton<TimeProvider>(new FixedClock());
        Services.AddScoped<ReviewScheduler>();
        Services.AddScoped<CardService>();
        Services.AddScoped<DeckService>();
    }

    [Fact]
    public async Task LoadingReadFailureRetryAndMissingDeckAreRecoverable()
    {
        var deck = await SeedAsync();
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        Assert.Contains("Loading", page.Find("[role='status']").TextContent);
        store.ReadGate.SetException(new IOException("private detail"));
        page.WaitForAssertion(() => Assert.Contains("Try again", page.Find("[role='alert']").TextContent));
        Assert.DoesNotContain("private detail", page.Markup);
        store.ReadGate = null;
        await page.Find("#retry-load").ClickAsync(new());
        Assert.Contains("No cards yet", page.Markup);

        page.Render(p => p.Add(x => x.DeckId, Guid.NewGuid()));

        Assert.Contains("This deck is no longer available", page.Markup);
        Assert.Empty(page.FindAll("#card-editor"));
        Assert.Equal("/decks", page.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task CreateShowsLabeledFieldsEscapesLiteralMarkupAndIsDueImmediately()
    {
        var deck = await SeedAsync();
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        const string front = "日本語 😀 <img src=x onerror=alert(1)>\nQuestion";
        var back = "<script>alert(1)</script>\n" + new string('a', 3900);
        Assert.Single(page.FindAll("label[for='card-front']"));
        Assert.Single(page.FindAll("label[for='card-back']"));
        foreach (var field in page.FindAll("textarea"))
        {
            Assert.True(field.HasAttribute("required"));
            Assert.Equal("4000", field.GetAttribute("maxlength"));
        }
        Fill(page, "  " + front + "  ", back);

        await page.Find("#card-editor").SubmitAsync();

        Assert.Equal(front, page.Find(".card-front").TextContent);
        Assert.Equal(back, page.Find(".card-back").TextContent);
        Assert.Empty(page.FindAll("img, script, [role='alert']"));
        Assert.Equal("", page.Find("#card-front").GetAttribute("value"));
        Assert.Contains("Card created", page.Find("[role='status']").TextContent);
        var saved = Assert.Single((await store.Inner.ReadSnapshotAsync()).Cards);
        Assert.Equal(new ReviewSchedule(0, Now), saved.Schedule);
        Assert.Equal(1, store.ReadCalls);
    }

    [Theory]
    [InlineData(false, "front", " ")]
    [InlineData(false, "back", " ")]
    [InlineData(false, "front", "long")]
    [InlineData(false, "back", "long")]
    [InlineData(true, "front", " ")]
    [InlineData(true, "back", " ")]
    [InlineData(true, "front", "long")]
    [InlineData(true, "back", "long")]
    public async Task CreateAndEditValidateBothFieldsInlineWithoutWriting(bool edit, string field, string value)
    {
        var deck = await SeedAsync(edit);
        var before = await store.Inner.ReadSnapshotAsync();
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        if (edit) page.Find(".edit-card").Click();
        value = value == "long" ? new string('x', 4001) : value;
        Fill(page, field == "front" ? value : "Valid", field == "back" ? value : "Valid");

        await page.Find("#card-editor").SubmitAsync();

        Assert.Single(page.FindAll($"#card-{field}-error[role='alert']"));
        Assert.Equal("true", page.Find($"#card-{field}").GetAttribute("aria-invalid"));
        Assert.Contains($"card-{field}-error", page.Find($"#card-{field}").GetAttribute("aria-describedby"));
        Assert.Equal(value, page.Find($"#card-{field}").GetAttribute("value"));
        Assert.Equal(before.Cards, (await store.Inner.ReadSnapshotAsync()).Cards);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveRetainsTextThenRetrySucceedsAndEditKeepsHistory(bool edit)
    {
        var deck = await SeedAsync(edit);
        var before = await store.Inner.ReadSnapshotAsync();
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        if (edit) page.Find(".edit-card").Click();
        var front = string.Concat(Enumerable.Repeat("😀", 2000));
        const string back = "日本語 <b>Answer</b>\nline 2";
        Fill(page, front, back);
        store.Inner.BeforeNextCommit = () => throw new IOException("private detail");

        await page.Find("#card-editor").SubmitAsync();

        Assert.Equal(front, page.Find("#card-front").GetAttribute("value"));
        Assert.Equal(back, page.Find("#card-back").GetAttribute("value"));
        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.Contains("Do not clear", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private detail", page.Markup);
        Assert.Equal(before.Cards, (await store.Inner.ReadSnapshotAsync()).Cards);

        await page.Find("#card-editor").SubmitAsync();

        var after = await store.Inner.ReadSnapshotAsync();
        var saved = Assert.Single(after.Cards);
        Assert.Equal(front, saved.Front);
        Assert.Equal(back, page.Find(".card-back").TextContent);
        Assert.Equal(before.Reviews, after.Reviews);
        if (edit)
        {
            Assert.Equal(before.Cards[0].Id, saved.Id);
            Assert.Equal(before.Cards[0].Schedule, saved.Schedule);
        }
        Assert.Empty(page.FindAll("[role='alert'], b"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSaveGuardsDuplicateAndConflictingActions(bool edit)
    {
        var deck = await SeedAsync(true);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        if (edit) page.Find(".edit-card").Click();
        Fill(page, "Changed", "Answer");
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = page.Find("#card-editor").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));
        await page.Find("#card-editor").SubmitAsync();
        await page.Find("#delete-deck").ClickAsync(new());
        await page.Find(".delete-card").ClickAsync(new());
        Assert.Empty(page.FindAll("#delete-confirmation"));
        Assert.True(page.Find("#card-editor fieldset").HasAttribute("disabled"));
        Assert.Equal(1, store.Writes);
        store.WriteGate.SetResult();
        await pending;

        Assert.Equal(edit ? 1 : 2, (await store.Inner.ReadSnapshotAsync()).Cards.Count);
    }

    [Fact]
    public async Task CancelEditDoesNotWriteAndMovesFocusBackToCards()
    {
        var deck = await SeedAsync(true);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        page.Find(".edit-card").Click();
        Assert.Single(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        Fill(page, "Discarded", "Discarded");

        page.Find("#cancel-edit").Click();

        Assert.Equal("Original", page.Find(".card-front").TextContent);
        Assert.Equal("", page.Find("#card-front").GetAttribute("value"));
        Assert.Equal(0, store.Writes);
        Assert.Equal(2, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeckDeleteNamesCurrentCountCancelDoesNothingAndFailedAtomicDeleteCanRetry(bool populated)
    {
        var deck = await SeedAsync(populated);
        var other = new Deck(Guid.NewGuid(), "Unrelated");
        await store.Inner.CreateDeckAsync(other);
        var otherCard = new Card(Guid.NewGuid(), other.Id, "Other", "Answer", new ReviewSchedule(0, Now));
        await store.Inner.CreateCardAsync(otherCard);
        var before = await store.Inner.ReadSnapshotAsync();
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));

        await page.Find("#delete-deck").ClickAsync(new());
        Assert.Contains(deck.Title, page.Find("#delete-confirmation").TextContent);
        Assert.Contains(populated ? "1 card" : "0 cards", page.Find("#delete-confirmation").TextContent);
        Assert.Contains("review history", page.Find("#delete-confirmation").TextContent);
        page.Find("#cancel-delete").Click();
        Assert.Equal(0, store.Writes);
        await page.Find("#delete-deck").ClickAsync(new());
        store.Inner.BeforeNextCommit = () => throw new IOException("private detail");

        await page.Find("#confirm-delete").ClickAsync(new());

        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private detail", page.Markup);
        var failed = await store.Inner.ReadSnapshotAsync();
        Assert.Equal(before.Decks, failed.Decks);
        Assert.Equal(before.Cards, failed.Cards);
        Assert.Equal(before.Reviews, failed.Reviews);
        Assert.DoesNotContain("/decks", Services.GetRequiredService<NavigationManager>().Uri.Replace("http://localhost/", ""));

        await page.Find("#confirm-delete").ClickAsync(new());

        var after = await store.Inner.ReadSnapshotAsync();
        Assert.Equal(other, Assert.Single(after.Decks));
        Assert.Equal(otherCard, Assert.Single(after.Cards));
        Assert.Empty(after.Reviews);
        Assert.EndsWith("/decks", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task CardDeleteIdentifiesCardCancelDoesNothingAndRetryRemovesOnlyItsHistory()
    {
        var deck = await SeedAsync(true);
        var sibling = new Card(Guid.NewGuid(), deck.Id, "Sibling", "Back", new ReviewSchedule(0, Now));
        await store.Inner.CreateCardAsync(sibling);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        page.Find(".delete-card").Click();
        Assert.Contains("Original", page.Find("#delete-confirmation").TextContent);
        page.Find("#cancel-delete").Click();
        Assert.Equal(0, store.Writes);
        page.Find(".delete-card").Click();
        store.Inner.BeforeNextCommit = () => throw new IOException("private detail");
        await page.Find("#confirm-delete").ClickAsync(new());
        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.Equal(2, (await store.Inner.ReadSnapshotAsync()).Cards.Count);

        await page.Find("#confirm-delete").ClickAsync(new());

        Assert.Equal(sibling, Assert.Single((await store.Inner.ReadSnapshotAsync()).Cards));
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Reviews);
        Assert.Single(page.FindAll(".card-row"));
        Assert.Empty(page.FindAll("#delete-confirmation, [role='alert']"));
    }

    [Fact]
    public async Task DeckConfirmationRefreshesCardCountAndFailedCountReadCanRetryWithoutLosingDraft()
    {
        var deck = await SeedAsync();
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        Fill(page, "Draft", "Draft answer");
        await store.Inner.CreateCardAsync(new Card(Guid.NewGuid(), deck.Id, "External", "Back", new ReviewSchedule(0, Now)));
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        store.ReadGate.SetException(new IOException("private detail"));

        await page.Find("#delete-deck").ClickAsync(new());

        Assert.Empty(page.FindAll("#delete-confirmation"));
        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private detail", page.Markup);
        Assert.Equal("Draft", page.Find("#card-front").GetAttribute("value"));
        store.ReadGate = null;

        await page.Find("#delete-deck").ClickAsync(new());

        Assert.Contains("1 card", page.Find("#delete-confirmation").TextContent);
        Assert.Equal("External", page.Find(".card-front").TextContent);
        Assert.True(page.Find("#card-editor fieldset").HasAttribute("disabled"));
        Assert.Single(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        page.Find("#cancel-delete").Click();
        Assert.Equal("Draft", page.Find("#card-front").GetAttribute("value"));
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingDeleteGuardsDuplicateConfirmCancelAndSave(bool entireDeck)
    {
        var deck = await SeedAsync(true);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        Fill(page, "Draft", "Draft answer");
        if (entireDeck) await page.Find("#delete-deck").ClickAsync(new());
        else page.Find(".delete-card").Click();
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = page.Find("#confirm-delete").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));
        await page.Find("#confirm-delete").ClickAsync(new());
        await page.Find("#cancel-delete").ClickAsync(new());
        await page.Find("#card-editor").SubmitAsync();
        Assert.Single(page.FindAll("#delete-confirmation"));
        Assert.True(page.Find("#cancel-delete").HasAttribute("disabled"));
        Assert.Equal(1, store.Writes);
        store.WriteGate.SetResult();
        await pending;

        var after = await store.Inner.ReadSnapshotAsync();
        Assert.Empty(after.Cards);
        Assert.Empty(after.Reviews);
        Assert.Equal(entireDeck ? 0 : 1, after.Decks.Count);
        if (!entireDeck) Assert.Equal("Draft", page.Find("#card-front").GetAttribute("value"));
    }

    [Fact]
    public async Task LeavingDetailDuringDeckDeleteKeepsNewPageAndCommittedDeletion()
    {
        var deck = await SeedAsync(true);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/decks/{deck.Id}");
        var page = Render<App>();
        await page.Find("#delete-deck").ClickAsync(new());
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = page.Find("#confirm-delete").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));

        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        Assert.Equal("Progress", page.Find("h1").TextContent);
        var focusCalls = JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        store.WriteGate.SetResult();
        await pending;

        var after = await store.Inner.ReadSnapshotAsync();
        Assert.Empty(after.Decks);
        Assert.Empty(after.Cards);
        Assert.Empty(after.Reviews);
        Assert.EndsWith("/progress", navigation.Uri);
        Assert.Equal("Progress", page.Find("h1").TextContent);
        Assert.Equal(focusCalls, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
        Assert.Empty(page.FindAll("#card-editor, #delete-confirmation, [role='alert']"));
    }

    [Fact]
    public async Task RouteChangeDuringDeckDeleteDoesNotNavigateAwayFromNewDeck()
    {
        var first = await SeedAsync(true);
        var second = new Deck(Guid.NewGuid(), "Second");
        await store.Inner.CreateDeckAsync(second);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, first.Id));
        await page.Find("#delete-deck").ClickAsync(new());
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = page.Find("#confirm-delete").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));

        page.Render(p => p.Add(x => x.DeckId, second.Id));
        store.WriteGate.SetResult();
        await pending;

        Assert.Equal("Second", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("#delete-confirmation, .card-row"));
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(second, Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks));
    }

    [Fact]
    public async Task RouteChangeClearsTextAndIgnoresOldReadCompletion()
    {
        var first = await SeedAsync(true);
        var second = new Deck(Guid.NewGuid(), "Second");
        await store.Inner.CreateDeckAsync(second);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, first.Id));
        page.Find(".edit-card").Click();
        Fill(page, "Old input", "Old answer");
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldGate = store.ReadGate;
        var oldLoad = page.InvokeAsync(() => page.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DeckDetail.DeckId)] = Guid.NewGuid() })));
        page.WaitForAssertion(() => Assert.Contains("Loading", page.Find("[role='status']").TextContent));
        store.ReadGate = null;

        page.Render(p => p.Add(x => x.DeckId, second.Id));
        oldGate.SetResult(new StudySnapshot([first], [], []));
        await oldLoad;

        Assert.Equal("Second", page.Find("h1").TextContent);
        Assert.Equal("", page.Find("#card-front").GetAttribute("value"));
        Assert.DoesNotContain("Original", page.Markup);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task RouteChangeDuringPendingSaveDoesNotAddOldCardToNewDeck()
    {
        var first = await SeedAsync();
        var second = new Deck(Guid.NewGuid(), "Second");
        await store.Inner.CreateDeckAsync(second);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, first.Id));
        Fill(page, "Old pending", "Answer");
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = page.Find("#card-editor").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));

        page.Render(p => p.Add(x => x.DeckId, second.Id));
        store.WriteGate.SetResult();
        await pending;

        Assert.Equal("Second", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll(".card-row"));
        Assert.Equal("", page.Find("#card-front").GetAttribute("value"));
        Assert.Equal(first.Id, Assert.Single((await store.Inner.ReadSnapshotAsync()).Cards).DeckId);
    }

    [Fact]
    public async Task ExternallyRemovedCardAndDeckShowUsefulRecovery()
    {
        var deck = await SeedAsync(true);
        var page = Render<DeckDetail>(p => p.Add(x => x.DeckId, deck.Id));
        page.Find(".edit-card").Click();
        Fill(page, "Retained", "Answer");
        await store.Inner.DeleteCardAsync((await store.Inner.ReadSnapshotAsync()).Cards[0].Id);
        await page.Find("#card-editor").SubmitAsync();
        Assert.Contains("no longer available", page.Find("[role='alert']").TextContent);
        Assert.Equal("Retained", page.Find("#card-front").GetAttribute("value"));
        page.Find("#cancel-edit").Click();
        await store.Inner.DeleteDeckAsync(deck.Id);
        Fill(page, "Retained again", "Answer");

        await page.Find("#card-editor").SubmitAsync();

        Assert.Contains("no longer available", page.Find("[role='alert']").TextContent);
        Assert.Equal("Retained again", page.Find("#card-front").GetAttribute("value"));
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Decks);
    }

    private async Task<Deck> SeedAsync(bool card = false)
    {
        var deck = new Deck(Guid.NewGuid(), "Biology", "Notes");
        await store.Inner.CreateDeckAsync(deck);
        if (card)
        {
            var item = new Card(Guid.NewGuid(), deck.Id, "Original", "Original back", new ReviewSchedule(0, Now));
            await store.Inner.CreateCardAsync(item);
            await store.Inner.ApplyReviewAsync(new ReviewEvent(Guid.NewGuid(), item.Id, Guid.NewGuid(), ReviewRating.Remembered, Now), new ReviewSchedule(3, Now.AddDays(7)));
        }
        return deck;
    }

    private static void Fill(IRenderedComponent<DeckDetail> page, string front, string back)
    {
        page.Find("#card-front").Input(front);
        page.Find("#card-back").Input(back);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ControlledStore : IStudyStore
    {
        public InMemoryStudyStore Inner { get; } = new();
        public TaskCompletionSource<StudySnapshot>? ReadGate { get; set; }
        public TaskCompletionSource? WriteGate { get; set; }
        public int ReadCalls { get; private set; }
        public int Writes { get; private set; }
        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return ReadGate?.Task ?? Inner.ReadSnapshotAsync(cancellationToken);
        }
        private async Task WriteAsync(Func<Task> write)
        {
            Writes++;
            if (WriteGate is not null) await WriteGate.Task;
            await write();
        }
        public Task CreateDeckAsync(Deck deck, CancellationToken token = default) => Inner.CreateDeckAsync(deck, token);
        public Task UpdateDeckAsync(Deck deck, CancellationToken token = default) => Inner.UpdateDeckAsync(deck, token);
        public Task DeleteDeckAsync(Guid id, CancellationToken token = default) => WriteAsync(() => Inner.DeleteDeckAsync(id, token));
        public Task CreateCardAsync(Card card, CancellationToken token = default) => WriteAsync(() => Inner.CreateCardAsync(card, token));
        public Task UpdateCardAsync(Card card, CancellationToken token = default) => WriteAsync(() => Inner.UpdateCardAsync(card, token));
        public Task DeleteCardAsync(Guid id, CancellationToken token = default) => WriteAsync(() => Inner.DeleteCardAsync(id, token));
        public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken token = default) => Inner.ApplyReviewAsync(review, schedule, token);
    }
}
