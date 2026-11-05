using System.Text.Json;
using Microsoft.JSInterop;
using RecallDeck.Web.Storage;

namespace RecallDeck.Core.Tests.Storage;

public sealed class IndexedDbInteropTests
{
    [Fact]
    public async Task SnapshotRehydratesValidatedImmutableCoreValues()
    {
        var deck = new Deck(Guid.NewGuid(), "日本語 😀");
        var card = new Card(Guid.NewGuid(), deck.Id, "front", "back", new ReviewSchedule(1, DateTimeOffset.UnixEpoch));
        var js = new Interop { Result = new { decks = new[] { deck }, cards = new[] { card }, reviews = Array.Empty<ReviewEvent>() } };

        var snapshot = await new IndexedDbStudyStore(js).ReadSnapshotAsync();

        Assert.Equal(deck, Assert.Single(snapshot.Decks));
        Assert.Equal(card, Assert.Single(snapshot.Cards));
        Assert.Equal("read", js.Operation);
        Assert.True(js.Disposed);
    }

    [Theory]
    [InlineData("missing", typeof(KeyNotFoundException))]
    [InlineData("duplicate", typeof(InvalidOperationException))]
    [InlineData("invalid", typeof(InvalidOperationException))]
    [InlineData("quota", typeof(IOException))]
    [InlineData("unavailable", typeof(IOException))]
    [InlineData("version", typeof(IOException))]
    [InlineData("storage", typeof(IOException))]
    public async Task FailedSavePreservesContractErrors(string error, Type expected)
    {
        var js = new Interop { Result = new { error } };

        var failure = await Record.ExceptionAsync(() => new IndexedDbStudyStore(js).DeleteCardAsync(Guid.NewGuid()));

        Assert.IsType(expected, failure);
        Assert.True(js.Disposed);
    }

    [Fact]
    public async Task CancellationBeforeOpeningDoesNotCallJavascript()
    {
        var js = new Interop();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IndexedDbStudyStore(js).ReadSnapshotAsync(cancellation.Token));

        Assert.False(js.Opened);
    }

    [Fact]
    public async Task CancellationAbortsPendingOperationAndAwaitsActualOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        var js = new Interop { BeforeFinish = cancellation.Cancel, Result = new { error = "canceled" } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IndexedDbStudyStore(js).DeleteDeckAsync(Guid.NewGuid(), cancellation.Token));

        Assert.True(js.Canceled);
        Assert.True(js.Disposed);
    }

    [Fact]
    public async Task CancellationAfterCommitDoesNotReportAFalseRollback()
    {
        using var cancellation = new CancellationTokenSource();
        var js = new Interop { BeforeFinish = cancellation.Cancel };

        await new IndexedDbStudyStore(js).DeleteDeckAsync(Guid.NewGuid(), cancellation.Token);

        Assert.True(js.Canceled);
        Assert.True(js.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JavascriptCancellationFailureDoesNotEscapeCancelOrReplaceActualOutcome(bool aborted)
    {
        using var cancellation = new CancellationTokenSource();
        var js = new Interop
        {
            ThrowOnCancel = true,
            BeforeFinish = cancellation.Cancel,
            Result = aborted ? new { error = "canceled" } : (object)new { }
        };

        var failure = await Record.ExceptionAsync(() => new IndexedDbStudyStore(js).DeleteDeckAsync(Guid.NewGuid(), cancellation.Token));

        if (aborted)
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        else
            Assert.Null(failure);
        Assert.True(js.Canceled);
        Assert.True(js.Disposed);
    }

    private sealed class Interop : IJSRuntime, IJSInProcessObjectReference
    {
        public object Result { get; init; } = new { };
        public Action? BeforeFinish { get; init; }
        public bool ThrowOnCancel { get; init; }
        public bool Opened { get; private set; }
        public bool Canceled { get; private set; }
        public bool Disposed { get; private set; }
        public string? Operation { get; private set; }

        public TValue Invoke<TValue>(string identifier, params object?[]? args)
        {
            if (identifier == "cancel")
            {
                Canceled = true;
                if (ThrowOnCancel) throw new JSException("Injected abort failure");
                return default!;
            }
            Assert.Equal("begin", identifier);
            Operation = (string)args![0]!;
            return (TValue)(object)this;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, params object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, params object?[]? args)
        {
            if (identifier == "import") { Opened = true; return ValueTask.FromResult((TValue)(object)this); }
            Assert.Equal("finish", identifier);
            BeforeFinish?.Invoke();
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(Result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }

        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
