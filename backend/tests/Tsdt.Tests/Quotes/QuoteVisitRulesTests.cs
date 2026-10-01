using Tsdt.Api.Quotes;
using Xunit;

namespace Tsdt.Tests.Quotes;

public sealed class QuoteVisitRulesTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Start_is_required_and_end_is_optional()
    {
        var start = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(-3));
        Assert.False(QuoteVisitRules.HasValidStart(default));
        Assert.True(QuoteVisitRules.HasValidStart(start));
        Assert.True(QuoteVisitRules.HasValidEnd(start, null));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Supplied_end_must_follow_start()
    {
        var start = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(-3));
        Assert.False(QuoteVisitRules.HasValidEnd(start, start));
        Assert.False(QuoteVisitRules.HasValidEnd(start, start.AddMinutes(-1)));
        Assert.True(QuoteVisitRules.HasValidEnd(start, start.AddMinutes(1)));
    }
}
