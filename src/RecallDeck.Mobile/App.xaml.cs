namespace RecallDeck.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return RecallDeck.Mobile.MainPage.CreateAppWindow("RecallDeck");
    }
}
