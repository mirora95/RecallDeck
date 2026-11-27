using Microsoft.Extensions.DependencyInjection;
using RecallDeck.UI;

namespace RecallDeck.Mobile;

public partial class MainPage : ContentPage
{
    private bool backPending;

    public MainPage()
    {
        InitializeComponent();
    }

    public static Window CreateAppWindow(string title)
    {
        var page = new MainPage();
        var window = new Window(page) { Title = title };
        window.Resumed += (_, _) => _ = page.ResumeAsync();
        return window;
    }

    protected override bool OnBackButtonPressed()
    {
        if (!backPending)
        {
            backPending = true;
            _ = GoBackAsync();
        }
        return true;
    }

    private async Task GoBackAsync()
    {
        try
        {
            var handled = true;
            var dispatched = await blazorWebView.TryDispatchAsync(services =>
                handled = services.GetRequiredService<AppNavigation>().GoBack());
            if (!dispatched)
            {
                await DisplayAlertAsync("Navigation unavailable", "Wait for the page to finish opening, then try Back again.", "OK");
                return;
            }
            if (!handled)
            {
#if ANDROID
                Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
            }
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Navigation unavailable", "RecallDeck could not go back. Your page is still here. Try again.", "OK");
        }
        finally
        {
            backPending = false;
        }
    }

    private async Task ResumeAsync()
    {
        try
        {
            var refresh = Task.CompletedTask;
            if (await blazorWebView.TryDispatchAsync(services =>
                refresh = services.GetRequiredService<AppLifecycle>().ResumeAsync()))
                await refresh;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"RecallDeck resume refresh failed: {error.GetType().Name}");
        }
    }
}
