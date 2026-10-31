namespace RecallDeck.Core;

internal static class PlainText
{
    internal static string Required(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ArgumentException($"Use at most {maximumLength} characters.", parameterName);

        return trimmed;
    }
}
