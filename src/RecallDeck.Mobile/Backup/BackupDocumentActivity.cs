using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;

namespace RecallDeck.Mobile.Backup;

/// <summary>Lifecycle-registered AndroidX result bridge shared by both Android hosts.</summary>
public class BackupDocumentActivity : MauiAppCompatActivity
{
    private ActivityResultLauncher? documents;
    private TaskCompletionSource<global::Android.Net.Uri?>? pending;
    private CancellationTokenRegistration cancellation;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        documents = RegisterForActivityResult(new ActivityResultContracts.StartActivityForResult(), new DocumentResult(this));
    }

    public Task<global::Android.Net.Uri?> SelectDocumentAsync(Intent intent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (pending is not null) throw new InvalidOperationException("A document selection is already open.");
        var completion = new TaskCompletionSource<global::Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        // Keep the pending slot until the picker returns, even if its page has left.
        cancellation = token.Register(() => completion.TrySetCanceled(token));
        try { documents!.Launch(intent); }
        catch
        {
            pending = null;
            cancellation.Dispose();
            throw;
        }
        return completion.Task;
    }

    private void Complete(ActivityResult result)
    {
        var completion = pending;
        pending = null;
        cancellation.Dispose();
        if (result.ResultCode == (int)Result.Ok && result.Data?.Data is { } uri)
            completion?.TrySetResult(uri);
        else
            completion?.TrySetResult(null);
    }

    protected override void OnDestroy()
    {
        pending?.TrySetException(new IOException("Document selection was interrupted. Try again."));
        pending = null;
        cancellation.Dispose();
        base.OnDestroy();
    }

    private sealed class DocumentResult(BackupDocumentActivity owner) : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result)
        {
            if (result is ActivityResult activityResult) owner.Complete(activityResult);
        }
    }
}
