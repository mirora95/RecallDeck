using System.Text;
using RecallDeck.Core.Backup;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;

namespace RecallDeck.Core.Tests.Backup;

public sealed class StudyBackupServiceTests
{
    [Fact]
    public async Task LargeValidUnicodeImportCanRestoreAndExportAgain()
    {
        var deck = new Deck(Guid.NewGuid(), "Unicode");
        var cards = Enumerable.Range(0, 1000).Select(_ => new Card(Guid.NewGuid(), deck.Id, new string('語', 2000), "answer <script> &",
            new ReviewSchedule(0, DateTimeOffset.UnixEpoch))).ToArray();
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, decks = new[] { deck }, cards, reviews = Array.Empty<ReviewEvent>() },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.True(bytes.Length < StudyBackupCodec.MaximumBytes);
        var store = new ObservedStore();
        var service = new StudyBackupService(store);
        var preview = service.Preview(bytes);

        await service.ConfirmRestoreAsync(preview);

        Assert.Equal(1000, (await store.Inner.ReadSnapshotAsync()).Cards.Count);
        var exportedBytes = await service.ExportCurrentAsync();
        Assert.DoesNotContain("<script>", Encoding.UTF8.GetString(exportedBytes), StringComparison.Ordinal);
        var exported = StudyBackupCodec.ValidateImport(exportedBytes);
        Assert.Equal(cards, exported.Cards);
    }

    [Fact]
    public void PreviewCanBeDiscardedWithoutReadingOrWritingStore()
    {
        var store = new ObservedStore();
        var bytes = StudyBackupCodec.Export(Fixture());
        var service = new StudyBackupService(store);

        var preview = service.Preview(bytes);
        Array.Fill(bytes, (byte)0);

        Assert.Equal(1, preview.DeckCount);
        Assert.Equal(1, preview.CardCount);
        Assert.Equal(1, preview.ReviewCount);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Replacements);
    }

    [Fact]
    public async Task ExplicitConfirmationUsesTheDetachedPreviewAndExportReadsCurrentData()
    {
        var store = new ObservedStore();
        var service = new StudyBackupService(store);
        var source = Fixture();
        var bytes = StudyBackupCodec.Export(source);
        var preview = service.Preview(bytes);
        Array.Fill(bytes, (byte)0);

        await service.ConfirmRestoreAsync(preview);
        var exported = StudyBackupCodec.ValidateImport(await service.ExportCurrentAsync());

        Assert.Equal(1, store.Replacements);
        Assert.Equal(1, store.Reads);
        AssertSnapshot(source, exported);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("dangling")]
    [InlineData("oversized")]
    public void InvalidPreviewNeverAccessesStore(string invalid)
    {
        var store = new ObservedStore();
        var source = Fixture();
        var text = Encoding.UTF8.GetString(StudyBackupCodec.Export(source));
        var root = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        if (invalid == "duplicate")
            root["decks"]!.AsArray().Add(root["decks"]![0]!.DeepClone());
        if (invalid == "dangling")
            root["cards"]![0]!["deckId"] = Guid.NewGuid().ToString();
        var bytes = invalid switch
        {
            "malformed" => Encoding.UTF8.GetBytes("{"),
            "null" => Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"decks\":null,\"cards\":[],\"reviews\":[]}"),
            "duplicate" or "dangling" => Encoding.UTF8.GetBytes(root.ToJsonString()),
            "oversized" => new byte[StudyBackupCodec.MaximumBytes + 1],
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        Assert.ThrowsAny<ArgumentException>(() => new StudyBackupService(store).Preview(bytes));

        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Replacements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledConfirmationPreservesDataAndPreviewCanRetry(bool cancel)
    {
        var store = new ObservedStore();
        var original = Fixture();
        await store.Inner.ReplaceAllAsync(original);
        var service = new StudyBackupService(store);
        var replacement = Fixture();
        var preview = service.Preview(StudyBackupCodec.Export(replacement));
        using var cancellation = new CancellationTokenSource();
        store.Inner.BeforeNextCommit = cancel ? cancellation.Cancel : () => throw new IOException("private storage failure");

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConfirmRestoreAsync(preview, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => service.ConfirmRestoreAsync(preview));

        AssertSnapshot(original, await store.Inner.ReadSnapshotAsync());
        await service.ConfirmRestoreAsync(preview);
        AssertSnapshot(replacement, await store.Inner.ReadSnapshotAsync());
        Assert.Equal(2, store.Replacements);
    }

    [Fact]
    public async Task PreCanceledConfirmationAndExportDoNotAccessStore()
    {
        var store = new ObservedStore();
        var service = new StudyBackupService(store);
        var preview = service.Preview(StudyBackupCodec.Export(Fixture()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConfirmRestoreAsync(preview, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportCurrentAsync(cancellation.Token));

        Assert.Equal(0, store.Replacements);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task EmptyPreviewCanBeConfirmedAndExported()
    {
        var store = new ObservedStore();
        await store.Inner.ReplaceAllAsync(Fixture());
        var service = new StudyBackupService(store);
        var preview = service.Preview(StudyBackupCodec.Export(new StudySnapshot([], [], [])));

        Assert.Equal(0, preview.DeckCount);
        Assert.Equal(0, preview.CardCount);
        Assert.Equal(0, preview.ReviewCount);
        await service.ConfirmRestoreAsync(preview);

        AssertSnapshot(new StudySnapshot([], [], []), StudyBackupCodec.ValidateImport(await service.ExportCurrentAsync()));
    }

    private static StudySnapshot Fixture()
    {
        var deck = new Deck(Guid.NewGuid(), "日本語 😀", "  notes\n");
        var card = new Card(Guid.NewGuid(), deck.Id, "<b>literal</b>", "answer\n😀", new ReviewSchedule(3, DateTimeOffset.UnixEpoch.AddTicks(1234567)));
        var review = new ReviewEvent(Guid.NewGuid(), card.Id, Guid.NewGuid(), ReviewRating.Again, DateTimeOffset.UnixEpoch.AddTicks(7));
        return new StudySnapshot([deck], [card], [review]);
    }

    private static void AssertSnapshot(StudySnapshot expected, StudySnapshot actual)
    {
        Assert.Equal(expected.Decks, actual.Decks);
        Assert.Equal(expected.Cards, actual.Cards);
        Assert.Equal(expected.Reviews, actual.Reviews);
    }

    private sealed class ObservedStore : IStudyStore
    {
        public InMemoryStudyStore Inner { get; } = new();
        public int Reads { get; private set; }
        public int Replacements { get; private set; }
        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Inner.ReadSnapshotAsync(cancellationToken);
        }
        public Task ReplaceAllAsync(StudySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Replacements++;
            return Inner.ReplaceAllAsync(snapshot, cancellationToken);
        }
        public Task CreateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateDeckAsync(Deck deck, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteDeckAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateCardAsync(Card card, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateCardAsync(Card card, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteCardAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplyReviewAsync(ReviewEvent review, ReviewSchedule schedule, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
