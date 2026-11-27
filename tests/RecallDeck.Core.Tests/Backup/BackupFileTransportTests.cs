using Bunit;
using Microsoft.JSInterop;
using RecallDeck.UI.Backup;
using RecallDeck.Web.Backup;

namespace RecallDeck.Core.Tests.Backup;

public sealed class BackupFileTransportTests : BunitContext
{
    [Fact]
    public async Task StreamReaderHandlesShortReadsAndBoundsNonSeekableInput()
    {
        using var source = new ShortStream(RecallDeck.Core.Backup.StudyBackupCodec.MaximumBytes + 100);

        await Assert.ThrowsAsync<ArgumentException>(() => BackupFileReader.ReadAsync(source));

        Assert.Equal(RecallDeck.Core.Backup.StudyBackupCodec.MaximumBytes + 1, source.Consumed);
        Assert.False(source.CanSeek);
    }

    [Fact]
    public async Task StreamReaderPreservesBytesAndHonorsCancellation()
    {
        using var source = new MemoryStream([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await BackupFileReader.ReadAsync(source));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var pending = new MemoryStream([1]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BackupFileReader.ReadAsync(pending, canceled.Token));
    }

    [Fact]
    public async Task BrowserSelectionPassesLimitAndReturnsTheSelectedBytesOrCancel()
    {
        var module = JSInterop.SetupModule("./backup-files.js");
        var selected = new BackupFile("owned.json", [1, 2, 3]);
        var call = module.Setup<BackupFile?>("openBackup", RecallDeck.Core.Backup.StudyBackupCodec.MaximumBytes);
        call.SetResult(selected);
        var files = new BrowserBackupFiles(JSInterop.JSRuntime);

        Assert.Equal(selected, await files.OpenAsync());
        call.SetResult(null);
        Assert.Null(await files.OpenAsync());
        Assert.Equal(2, module.Invocations["openBackup"].Count);
    }

    [Fact]
    public async Task BrowserDownloadAcknowledgesOnlyRequestAndPropagatesFailure()
    {
        var module = JSInterop.SetupModule("./backup-files.js");
        module.SetupVoid("saveBackup", _ => true).SetVoidResult();
        var files = new BrowserBackupFiles(JSInterop.JSRuntime);

        Assert.Equal(BackupSaveResult.DownloadRequested, await files.SaveAsync("owned.json", [1, 2, 3]));
        Assert.Equal("owned.json", module.Invocations["saveBackup"][0].Arguments[0]);
        Assert.Equal(new byte[] { 1, 2, 3 }, module.Invocations["saveBackup"][0].Arguments[1]);
    }

    [Fact]
    public async Task PreCanceledBrowserOperationNeverCallsJavaScript()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var files = new BrowserBackupFiles(JSInterop.JSRuntime);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.OpenAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.SaveAsync("owned.json", [], canceled.Token));
        Assert.Empty(JSInterop.Invocations);
    }

    private sealed class ShortStream(int length) : Stream
    {
        public int Consumed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, 8191), length - Consumed);
            buffer.Span[..count].Fill(42);
            Consumed += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
