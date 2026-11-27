using RecallDeck.Core.Backup;

namespace RecallDeck.UI.Backup;

public static class BackupFileReader
{
    /// <summary>Reads at most the limit plus one sentinel byte, including non-seekable URI streams.</summary>
    public static async Task<byte[]> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[StudyBackupCodec.MaximumBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count > StudyBackupCodec.MaximumBytes)
            throw new ArgumentException("Use at most 10 MiB of UTF-8 JSON.");
        return buffer.AsSpan(0, count).ToArray();
    }
}
