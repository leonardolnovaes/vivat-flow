namespace Tsdt.Api.Identity;

public static class LocaleRules
{
    public const string BrazilianPortuguese = "pt-BR";
    public const string AmericanEnglish = "en-US";

    public static bool IsSupported(string? locale) => locale is BrazilianPortuguese or AmericanEnglish;
}
