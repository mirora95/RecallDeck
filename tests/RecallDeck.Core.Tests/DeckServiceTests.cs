using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests;

public class DeckServiceTests
{
    [Fact]
    public async Task CreateReturnsThePersistedValidatedDeck()
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);

        var created = await service.CreateAsync("  日本語 😀  ", "Notes\nline 2");
        var persisted = Assert.Single((await store.ReadSnapshotAsync()).Decks);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("日本語 😀", created.Title);
        Assert.Equal("Notes\nline 2", created.Description);
        Assert.Equal(created, persisted);
    }

    [Fact]
    public async Task RenamePreservesIdDescriptionCardsAndHistory()
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);
        var created = await service.CreateAsync("Original", "Retained notes");
        var card = new Card(Guid.NewGuid(), created.Id, "front", "back", new ReviewSchedule(0, DateTimeOffset.UnixEpoch));
        await store.CreateCardAsync(card);
        await store.ApplyReviewAsync(new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), ReviewRating.Remembered, DateTimeOffset.UnixEpoch), new ReviewSchedule(1, DateTimeOffset.UnixEpoch.AddDays(1)));
        var before = await store.ReadSnapshotAsync();

        var renamed = await service.RenameAsync(created.Id, "  New title  ");
        var after = await store.ReadSnapshotAsync();

        Assert.Equal(created.Id, renamed.Id);
        Assert.Equal("New title", renamed.Title);
        Assert.Equal("Retained notes", renamed.Description);
        Assert.Equal(renamed, Assert.Single(after.Decks));
        Assert.Equal(before.Cards, after.Cards);
        Assert.Equal(before.Reviews, after.Reviews);
        Assert.Equal("Original", created.Title);
    }

    [Fact]
    public async Task ListUsesOrdinalTitleThenIdWithoutMutatingStorageOrder()
    {
        var store = new InMemoryStudyStore();
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var insertionOrder = new[]
        {
            new Deck(Guid.NewGuid(), "alpha"),
            new Deck(secondId, "Alpha"),
            new Deck(Guid.NewGuid(), "Zulu"),
            new Deck(firstId, "Alpha")
        };
        foreach (var deck in insertionOrder)
            await store.CreateDeckAsync(deck);

        var result = await new DeckService(store).ListAsync();

        Assert.Equal(new[] { firstId, secondId, insertionOrder[2].Id, insertionOrder[0].Id }, result.Select(deck => deck.Id));
        Assert.Equal(insertionOrder, (await store.ReadSnapshotAsync()).Decks);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<Deck>>(result).Clear());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task MissingTitleProvidesFieldFeedbackWithoutChangingData(string? title)
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);
        var existing = await service.CreateAsync("Original");

        var createError = await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(title!));
        var renameError = await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RenameAsync(existing.Id, title!));

        Assert.Equal("title", createError.ParamName);
        Assert.Equal("title", renameError.ParamName);
        Assert.Equal(existing, Assert.Single((await store.ReadSnapshotAsync()).Decks));
    }

    [Fact]
    public async Task OverlongFieldsAreRejectedWithoutWriting()
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);
        var existing = await service.CreateAsync("Original");

        Assert.Equal("title", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new string('x', 81)))).ParamName);
        Assert.Equal("title", (await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(existing.Id, new string('x', 81)))).ParamName);
        Assert.Equal("description", (await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("Valid", new string('x', 501)))).ParamName);

        Assert.Equal(existing, Assert.Single((await store.ReadSnapshotAsync()).Decks));
    }

    [Fact]
    public async Task RenamingMissingDeckDoesNotCreateIt()
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RenameAsync(Guid.NewGuid(), "Missing"));

        Assert.Empty(await service.ListAsync());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    public async Task FailedPersistenceDoesNotReturnSuccessOrChangeOldData(string operation)
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);
        var existing = await service.CreateAsync("Original");
        store.BeforeNextCommit = () => throw new IOException("Injected save failure");

        await Assert.ThrowsAsync<IOException>(() => operation == "create"
            ? service.CreateAsync("New")
            : service.RenameAsync(existing.Id, "Changed"));

        Assert.Equal(existing, Assert.Single(await service.ListAsync()));
    }

    [Fact]
    public async Task CanceledOperationsPropagateAndKeepExistingData()
    {
        var store = new InMemoryStudyStore();
        var service = new DeckService(store);
        var existing = await service.CreateAsync("Original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync("New", cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RenameAsync(existing.Id, "Changed", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ListAsync(cancellation.Token));

        Assert.Equal(existing, Assert.Single(await service.ListAsync()));
    }
}
