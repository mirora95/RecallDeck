using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;
using RecallDeck.UI.Pages;

namespace RecallDeck.Core.Tests;

public class DecksTests : BunitContext
{
    private readonly ControlledStore store = new();

    public DecksTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IStudyStore>(store);
        Services.AddScoped<DeckService>();
    }

    [Fact]
    public void EmptyStateHasLabeledCreateFields()
    {
        var page = Render<Decks>();

        Assert.Contains("No decks yet", page.Find("#deck-list").TextContent);
        Assert.Equal("create-title", page.Find("label[for='create-title']").GetAttribute("for"));
        Assert.Single(page.FindAll("label[for='create-description']"));
        Assert.Empty(page.FindAll("[role='alert']"));
    }

    [Fact]
    public async Task LoadingAndFailedReadOfferRetryWithoutLeakingDetails()
    {
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Render<Decks>();
        Assert.Contains("Loading decks", page.Find("[role='status']").TextContent);
        Assert.Empty(page.FindAll("#create-deck"));

        store.ReadGate.SetException(new IOException("private storage detail"));
        page.WaitForAssertion(() => Assert.Contains("Try again", page.Find("[role='alert']").TextContent));
        Assert.DoesNotContain("private storage detail", page.Markup);
        AssertStorageRecoveryAdvice(page.Find("[role='alert']").TextContent);
        store.ReadGate = null;
        await page.Find("button").ClickAsync(new());
        Assert.Contains("No decks yet", page.Markup);
    }

    [Fact]
    public async Task CreateTrimsTitleEscapesUnicodeAndDoesNotReadAgainAfterSave()
    {
        var page = Render<Decks>();
        const string title = "日本語 😀 <img src=x onerror=alert(1)>";
        page.Find("#create-title").Input($"  {title}  ");
        page.Find("#create-description").Input("Notes\n<script>alert(1)</script>");
        store.FailReads = true;

        await page.Find("#create-deck").SubmitAsync();

        Assert.Equal(title, page.Find(".deck-row h3").TextContent);
        Assert.Contains("<script>alert(1)</script>", page.Find(".deck-row p").TextContent);
        Assert.Empty(page.FindAll("img, script, [role='alert']"));
        Assert.Equal("", page.Find("#create-title").GetAttribute("value"));
        Assert.Equal("", page.Find("#create-description").GetAttribute("value"));
        Assert.Equal(1, store.ReadCalls);
        Assert.Equal(title, Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks).Title);
    }

    [Theory]
    [InlineData(" ", "", "title")]
    [InlineData("long-title", "", "title")]
    [InlineData("Valid", "long-description", "description")]
    public async Task CreateValidationIsInlineAndPreservesInput(string title, string description, string field)
    {
        title = title == "long-title" ? new string('x', 81) : title;
        description = description == "long-description" ? new string('x', 501) : description;
        var page = Render<Decks>();
        page.Find("#create-title").Input(title);
        page.Find("#create-description").Input(description);

        await page.Find("#create-deck").SubmitAsync();

        Assert.Single(page.FindAll($"#create-{field}-error[role='alert']"));
        Assert.Equal("true", page.Find($"#create-{field}").GetAttribute("aria-invalid"));
        Assert.Contains($"create-{field}-error", page.Find($"#create-{field}").GetAttribute("aria-describedby"));
        Assert.Equal(title, page.Find("#create-title").GetAttribute("value"));
        Assert.Equal(description, page.Find("#create-description").GetAttribute("value"));
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Decks);
    }

    [Fact]
    public async Task FailedCreateRetainsFieldsAndCanBeRetried()
    {
        var page = Render<Decks>();
        page.Find("#create-title").Input("Biology");
        page.Find("#create-description").Input("Cell notes");
        store.Inner.BeforeNextCommit = () => throw new IOException("private storage detail");

        await page.Find("#create-deck").SubmitAsync();

        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private storage detail", page.Markup);
        AssertStorageRecoveryAdvice(page.Find("[role='alert']").TextContent);
        Assert.Equal("Biology", page.Find("#create-title").GetAttribute("value"));
        Assert.Equal("Cell notes", page.Find("#create-description").GetAttribute("value"));
        Assert.Empty((await store.Inner.ReadSnapshotAsync()).Decks);

        await page.Find("#create-deck").SubmitAsync();

        Assert.Equal("Biology", page.Find(".deck-row h3").TextContent);
        Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks);
        Assert.Empty(page.FindAll("[role='alert']"));
    }

    [Fact]
    public async Task DoubleCreateSubmitWritesOnlyOnceWhilePending()
    {
        var page = Render<Decks>();
        page.Find("#create-title").Input("Biology");
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = page.Find("#create-deck").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, store.CreateCalls));
        Assert.True(page.Find("#create-deck fieldset").HasAttribute("disabled"));
        await page.Find("#create-deck").SubmitAsync();
        Assert.Equal(1, store.CreateCalls);
        store.WriteGate.SetResult();
        await first;

        Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks);
    }

    [Fact]
    public async Task RenameValidationFailureRetryAndCancelPreserveStoredData()
    {
        var original = new Deck(Guid.NewGuid(), "Biology", "Cell notes");
        await store.Inner.CreateDeckAsync(original);
        var page = Render<Decks>();
        page.Find(".deck-row button").Click();
        Assert.Single(page.FindAll("label[for='rename-title']"));
        page.Find("#rename-title").Input(" ");
        await page.Find("#rename-deck").SubmitAsync();
        Assert.Single(page.FindAll("#rename-title-error[role='alert']"));

        page.Find("#rename-title").Input("  日本語 <b>New</b>  ");
        store.Inner.BeforeNextCommit = () => throw new IOException("private storage detail");
        await page.Find("#rename-deck").SubmitAsync();
        Assert.Equal("  日本語 <b>New</b>  ", page.Find("#rename-title").GetAttribute("value"));
        Assert.Contains("Try again", page.Find("[role='alert']").TextContent);
        Assert.DoesNotContain("private storage detail", page.Markup);
        AssertStorageRecoveryAdvice(page.Find("[role='alert']").TextContent);
        Assert.Equal(original, Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks));

        await page.Find("#rename-deck").SubmitAsync();
        var renamed = Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks);
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(original.Description, renamed.Description);
        Assert.Equal("日本語 <b>New</b>", page.Find(".deck-row h3").TextContent);
        Assert.Empty(page.FindAll("b, #rename-deck, [role='alert']"));

        page.Find(".deck-row button").Click();
        page.Find("#rename-title").Input("Discarded");
        page.Find("#rename-deck button[type='button']").Click();
        Assert.Empty(page.FindAll("#rename-deck"));
        Assert.Equal(renamed, Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks));
    }

    [Fact]
    public async Task DoubleRenameSubmitWritesOnlyOnceAndReordersTheList()
    {
        await store.Inner.CreateDeckAsync(new Deck(Guid.NewGuid(), "Biology"));
        await store.Inner.CreateDeckAsync(new Deck(Guid.NewGuid(), "Zoology"));
        var page = Render<Decks>();
        page.FindAll(".deck-row button")[1].Click();
        page.Find("#rename-title").Input("Anatomy");
        store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = page.Find("#rename-deck").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, store.UpdateCalls));
        Assert.True(page.Find("#rename-deck fieldset").HasAttribute("disabled"));
        await page.Find("#rename-deck").SubmitAsync();
        Assert.Equal(1, store.UpdateCalls);
        store.WriteGate.SetResult();
        await first;

        Assert.Equal(new[] { "Anatomy", "Biology" }, page.FindAll(".deck-row h3").Select(x => x.TextContent));
    }

    [Fact]
    public async Task RenameMovesFocusToTheEditorAndBackToTheListOnCancelOrSave()
    {
        await store.Inner.CreateDeckAsync(new Deck(Guid.NewGuid(), "Biology"));
        var page = Render<Decks>();

        page.Find(".deck-row button").Click();
        Assert.Single(JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        page.Find("#rename-deck button[type='button']").Click();
        Assert.Equal(2, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
        Assert.Equal("-1", page.Find("#decks-heading").GetAttribute("tabindex"));

        page.Find(".deck-row button").Click();
        page.Find("#rename-title").Input("Anatomy");
        await page.Find("#rename-deck").SubmitAsync();
        Assert.Equal(4, JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
    }

    private static void AssertStorageRecoveryAdvice(string message)
    {
        Assert.Contains("free space outside this app", message);
        Assert.Contains("allow site storage", message);
        Assert.Contains("copy unsaved text", message);
        Assert.Contains("compatible updated app", message);
        Assert.Contains("Do not clear this app's data", message);
    }

    private sealed class ControlledStore : IStudyStore
    {
        public InMemoryStudyStore Inner { get; } = new();
        public TaskCompletionSource<StudySnapshot>? ReadGate { get; set; }
        public TaskCompletionSource? WriteGate { get; set; }
        public bool FailReads { get; set; }
        public int ReadCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return FailReads ? Task.FromException<StudySnapshot>(new IOException("private storage detail"))
                : ReadGate?.Task ?? Inner.ReadSnapshotAsync(cancellationToken);
        }

        public async Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            if (WriteGate is not null) await WriteGate.Task;
            await Inner.CreateDeckAsync(deck, cancellationToken);
        }

        public async Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            if (WriteGate is not null) await WriteGate.Task;
            await Inner.UpdateDeckAsync(deck, cancellationToken);
        }

        public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default) => Inner.DeleteDeckAsync(id, cancellationToken);
        public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default) => Inner.CreateCardAsync(card, cancellationToken);
        public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default) => Inner.UpdateCardAsync(card, cancellationToken);
        public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default) => Inner.DeleteCardAsync(id, cancellationToken);
        public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default) => Inner.ApplyReviewAsync(review, schedule, cancellationToken);
    }
}
