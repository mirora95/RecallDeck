namespace RecallDeck.Android.UiSmoke;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        RecallDeck.Mobile.MainPage.CreateAppWindow("RecallDeck UI checks");
}
