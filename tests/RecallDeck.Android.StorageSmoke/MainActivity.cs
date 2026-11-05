using Android.App;
using Android.OS;
using Android.Widget;
using RecallDeck.Storage.Checks;

namespace RecallDeck.Android.StorageSmoke;

[Activity(Name = "io.github.mirora95.recalldeck.storagesmoke.MainActivity", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var output = new TextView(this) { Text = "Running isolated storage checks…" };
        SetContentView(output);
        var directory = FilesDir!.AbsolutePath;
        var path = Path.Combine(directory, "verification.db3");
        string result;
        try
        {
            if (!File.Exists(path))
            {
                await SqliteSmoke.SeedAsync(path);
                result = "PASS phase 1: native SQLite saved Unicode, identities, UTC schedule and history. Force-stop and reopen this test app.";
            }
            else
            {
                await SqliteSmoke.VerifyRestartAndRollbackAsync(path);
                File.Delete(path);
                result = "PASS phase 2: process restart, native SQLite rollback, retry and cascade. Synthetic database removed.";
            }
        }
        catch (Exception error) { result = "FAIL: " + error; }
        File.WriteAllText(Path.Combine(directory, "result.txt"), result);
        output.Text = result;
    }
}
