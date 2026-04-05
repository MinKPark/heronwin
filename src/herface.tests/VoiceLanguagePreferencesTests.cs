using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class VoiceLanguagePreferencesTests
{
    [Fact]
    public void Parse_ReturnsAmericanEnglishAndKoreanByDefault()
    {
        var actual = VoiceLanguagePreferences.Parse(raw: null);

        Assert.Collection(
            actual,
            language =>
            {
                Assert.Equal("American English", language.DisplayName);
                Assert.Equal("en", language.OpenAiLanguageCode);
            },
            language =>
            {
                Assert.Equal("Korean", language.DisplayName);
                Assert.Equal("ko", language.OpenAiLanguageCode);
            });
    }

    [Fact]
    public void Parse_NormalizesKnownLanguagesAndDeduplicatesAliases()
    {
        var actual = VoiceLanguagePreferences.Parse("en-US, Korean, english, ko-KR");

        Assert.Collection(
            actual,
            language =>
            {
                Assert.Equal("American English", language.DisplayName);
                Assert.Equal("en", language.OpenAiLanguageCode);
            },
            language =>
            {
                Assert.Equal("Korean", language.DisplayName);
                Assert.Equal("ko", language.OpenAiLanguageCode);
            });
    }

    [Fact]
    public void BuildTranscriptionPrompt_PreservesMixedLanguageGuidance()
    {
        var languages = VoiceLanguagePreferences.Parse("American English, Korean");

        var actual = VoiceLanguagePreferences.BuildTranscriptionPrompt(languages);

        Assert.Contains("American English", actual, StringComparison.Ordinal);
        Assert.Contains("Korean", actual, StringComparison.Ordinal);
        Assert.Contains("switch naturally between them", actual, StringComparison.OrdinalIgnoreCase);
    }
}
