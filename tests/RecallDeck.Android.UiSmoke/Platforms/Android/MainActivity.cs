using Android.App;
using Android.Content.PM;

namespace RecallDeck.Android.UiSmoke;

[Activity(Name = "io.github.mirora95.recalldeck.uismoke.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity { }
