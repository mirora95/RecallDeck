using Microsoft.AspNetCore.Components.WebView.Maui;

namespace RecallDeck.Android.UiSmoke;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(RecallDeck.UI.App) });
        return new Window(new ContentPage { Content = view }) { Title = "RecallDeck UI checks" };
    }
}
