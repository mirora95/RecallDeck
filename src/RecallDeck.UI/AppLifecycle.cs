namespace RecallDeck.UI;

public sealed class AppLifecycle
{
    public event Func<Task>? Resumed;
    private Task? pending;

    public Task ResumeAsync() => pending is { IsCompleted: false } ? pending : pending = NotifyAsync();

    private async Task NotifyAsync()
    {
        if (Resumed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}
