namespace Tsdt.Api.Quotes;

public static class QuoteVisitRules
{
    public static bool HasValidStart(DateTimeOffset start) => start != default;
    public static bool HasValidEnd(DateTimeOffset start, DateTimeOffset? end) => !end.HasValue || end.Value > start;
}
