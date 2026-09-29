using Tsdt.Api.Identity;

namespace Tsdt.Tests.Identity;

[Trait("Category", "Unit")]
public sealed class LocaleRulesTests
{
    [Theory]
    [InlineData("pt-BR", true)]
    [InlineData("en-US", true)]
    [InlineData("pt", false)]
    [InlineData("en-GB", false)]
    [InlineData("es-ES", false)]
    public void IsSupported_accepts_only_persistable_locales(string locale, bool expected) => Assert.Equal(expected, LocaleRules.IsSupported(locale));
}
