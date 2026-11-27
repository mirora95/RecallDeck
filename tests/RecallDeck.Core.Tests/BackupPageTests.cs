using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RecallDeck.Core.Backup;
using RecallDeck.Core.Storage;
using RecallDeck.Core.Tests.Storage;
using RecallDeck.UI;
using RecallDeck.UI.Backup;
using BackupPage = RecallDeck.UI.Pages.Backup;
using RecallDeck.UI.Pages;

namespace RecallDeck.Core.Tests;

public sealed class BackupPageTests : BunitContext
{
    private readonly Files files = new();
    private readonly Store store = new();
    private static readonly byte[] Empty = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"decks\":[],\"cards\":[],\"reviews\":[]}");

    public BackupPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IStudyStore>(store);
        Services.AddSingleton<IBackupFiles>(files);
        Services.AddScoped<StudyBackupService>();
        Services.AddSingleton<TimeProvider>(new Clock());
    }

    [Fact]
    public async Task PreviewIsReadOnlyShowsCountsAndWarnsBeforeOneExplicitReplacement()
    {
        var deck = new Deck(Guid.NewGuid(), "<script>日本語 😀</script>");
        files.Next = new("owned.json", StudyBackupCodec.Export(new([deck], [], [])));
        var page = Render<BackupPage>();

        await page.Find("#choose-backup").ClickAsync(new());

        Assert.Equal(0, store.Writes);
        Assert.Contains("1 decks", page.Find("#restore-counts").TextContent);
        Assert.Contains("0 cards", page.Find("#restore-counts").TextContent);
        Assert.Contains("0 reviews", page.Find("#restore-counts").TextContent);
        Assert.Contains("ALL current", page.Find("#restore-warning").TextContent);
        Assert.Single(page.FindAll("#export-backup"));
        Assert.Empty(page.FindAll("script"));
        await page.Find("#confirm-restore").ClickAsync(new());

        Assert.Equal(1, store.Writes);
        Assert.Equal(deck, Assert.Single((await store.Inner.ReadSnapshotAsync()).Decks));
        Assert.Empty(page.FindAll("#confirm-restore"));
        Assert.Contains("Restore complete", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAndOversizedFilesNeverReachConfirmationOrStorage(bool oversized)
    {
        files.Next = new("owned.json", oversized ? new byte[StudyBackupCodec.MaximumBytes + 1] : Encoding.UTF8.GetBytes("{}"));
        var page = Render<BackupPage>();

        await page.Find("#choose-backup").ClickAsync(new());

        Assert.Single(page.FindAll("[role=alert]"));
        Assert.Empty(page.FindAll("#confirm-restore"));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task EmptyWarningAndCancelDiscardPreviewWithoutWriting()
    {
        files.Next = new("empty.json", Empty);
        var page = Render<BackupPage>();
        await page.Find("#choose-backup").ClickAsync(new());
        Assert.Contains("empty", page.Find("#empty-warning").TextContent);

        await page.Find("#cancel-restore").ClickAsync(new());

        Assert.Equal(0, store.Writes);
        Assert.Empty(page.FindAll("#restore-counts, #confirm-restore"));
    }

    [Fact]
    public async Task CanceledPickerDoesNotWriteAndAllowsAnotherChoice()
    {
        var page = Render<BackupPage>();
        await page.Find("#choose-backup").ClickAsync(new());

        Assert.Contains("Selection canceled", page.Markup);
        Assert.Equal(0, store.Writes);
        files.Next = new("empty.json", Empty);
        await page.Find("#choose-backup").ClickAsync(new());
        Assert.Single(page.FindAll("#confirm-restore"));
    }

    [Theory]
    [InlineData(BackupSaveResult.Saved, "Backup saved")]
    [InlineData(BackupSaveResult.DownloadRequested, "Download requested")]
    [InlineData(BackupSaveResult.Canceled, "Export canceled")]
    public async Task ExportUsesDatedValidatedJsonAndTruthfulHostResult(BackupSaveResult result, string message)
    {
        files.Result = result;
        var page = Render<BackupPage>();

        await page.Find("#export-backup").ClickAsync(new());

        Assert.Equal("recalldeck-2026-11-27.json", files.SavedName);
        Assert.NotNull(StudyBackupCodec.ValidateImport(files.SavedBytes!));
        Assert.Contains(message, page.Markup);
        Assert.Equal(0, store.Writes);
        if (result == BackupSaveResult.DownloadRequested)
            Assert.Contains("cannot confirm", page.Markup);
    }

    [Fact]
    public async Task FailedRestoreRetainsPreviewAndRetriesOnlyOnAnotherExplicitConfirm()
    {
        files.Next = new("empty.json", Empty);
        var page = Render<BackupPage>();
        await page.Find("#choose-backup").ClickAsync(new());
        store.Fail = true;

        await page.Find("#confirm-restore").ClickAsync(new());

        Assert.Equal(1, store.Writes);
        Assert.Single(page.FindAll("#restore-counts"));
        Assert.Single(page.FindAll("[role=alert]"));
        Assert.DoesNotContain("private failure", page.Markup);
        store.Fail = false;
        await page.Find("#confirm-restore").ClickAsync(new());
        Assert.Equal(2, store.Writes);
        Assert.Contains("Restore complete", page.Markup);
    }

    [Fact]
    public async Task BusyGuardsPreventDuplicateConfirmAndOtherFileActions()
    {
        files.Next = new("empty.json", Empty);
        var page = Render<BackupPage>();
        await page.Find("#choose-backup").ClickAsync(new());
        store.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = page.Find("#confirm-restore").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, store.Writes));
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        await page.Find("#confirm-restore").ClickAsync(new());
        Assert.Equal(1, store.Writes);
        store.Gate.SetResult();
        await first;
    }

    [Fact]
    public async Task LeavingDuringSelectionIgnoresLatePreviewAndNeverWrites()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/backup");
        var page = Render<App>();
        files.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = page.Find("#choose-backup").ClickAsync(new());
        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        files.Gate.SetResult(new("empty.json", Empty));
        await pending;

        Assert.Equal(0, store.Writes);
        Assert.True(files.Token.IsCancellationRequested);
        Assert.Equal("Progress", page.Find("h1").TextContent);
    }

    [Fact]
    public async Task LeavingDuringExportReadDoesNotStartFileSave()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/backup");
        var page = Render<App>();
        store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = page.Find("#export-backup").ClickAsync(new());
        await page.InvokeAsync(() => navigation.NavigateTo("/progress"));
        store.ReadGate.SetResult(new([], [], []));
        await pending;

        Assert.Null(files.SavedName);
        Assert.Equal("Progress", page.Find("h1").TextContent);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    }

    public sealed class Files : IBackupFiles
    {
        public BackupFile? Next { get; set; }
        public TaskCompletionSource<BackupFile?>? Gate { get; set; }
        public BackupSaveResult Result { get; set; } = BackupSaveResult.DownloadRequested;
        public CancellationToken Token { get; private set; }
        public string? SavedName { get; private set; }
        public byte[]? SavedBytes { get; private set; }
        public Task<BackupFile?> OpenAsync(CancellationToken token = default)
        {
            Token = token;
            return Gate?.Task ?? Task.FromResult(Next);
        }
        public Task<BackupSaveResult> SaveAsync(string name, byte[] content, CancellationToken token = default)
        {
            SavedName = name;
            SavedBytes = content;
            return Task.FromResult(Result);
        }
    }

    private sealed class Store : IStudyStore
    {
        public InMemoryStudyStore Inner { get; } = new();
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource<StudySnapshot>? ReadGate { get; set; }
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public Task<StudySnapshot> ReadSnapshotAsync(CancellationToken t = default) => ReadGate?.Task ?? Inner.ReadSnapshotAsync(t);
        public async Task ReplaceAllAsync(StudySnapshot snapshot, CancellationToken t = default)
        {
            Writes++;
            if (Gate is not null) await Gate.Task;
            if (Fail) throw new IOException("private failure");
            await Inner.ReplaceAllAsync(snapshot, t);
        }
        public Task CreateDeckAsync(Deck d, CancellationToken t = default) => Inner.CreateDeckAsync(d, t);
        public Task UpdateDeckAsync(Deck d, CancellationToken t = default) => Inner.UpdateDeckAsync(d, t);
        public Task DeleteDeckAsync(Guid id, CancellationToken t = default) => Inner.DeleteDeckAsync(id, t);
        public Task CreateCardAsync(Card c, CancellationToken t = default) => Inner.CreateCardAsync(c, t);
        public Task UpdateCardAsync(Card c, CancellationToken t = default) => Inner.UpdateCardAsync(c, t);
        public Task DeleteCardAsync(Guid id, CancellationToken t = default) => Inner.DeleteCardAsync(id, t);
        public Task ApplyReviewAsync(ReviewEvent r, ReviewSchedule s, CancellationToken t = default) => Inner.ApplyReviewAsync(r, s, t);
    }
}
