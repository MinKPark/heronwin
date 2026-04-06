namespace HeronWin.Brain;

internal sealed record VoiceLanguagePreference(
    string DisplayName,
    string? OpenAiLanguageCode);

internal static class VoiceLanguagePreferences
{
    private static readonly VoiceLanguagePreference AmericanEnglish = new("American English", "en");
    private static readonly VoiceLanguagePreference Korean = new("Korean", "ko");

    internal static IReadOnlyList<VoiceLanguagePreference> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GetDefaultLanguages();
        }

        var parsed = raw
            .Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseSingle)
            .DistinctBy(GetDistinctKey)
            .ToList();

        return parsed.Count > 0 ? parsed : GetDefaultLanguages();
    }

    internal static string BuildTranscriptionPrompt(IReadOnlyList<VoiceLanguagePreference> languages)
    {
        var effectiveLanguages = languages.Count > 0 ? languages : GetDefaultLanguages();
        var labels = effectiveLanguages.Select(language => language.DisplayName).ToArray();
        if (labels.Length == 1)
        {
            return $"Context: primary spoken language = {labels[0]}. Preserve names and short command phrases.";
        }

        return $"Context: primary spoken languages = {string.Join(", ", labels)}. Mixed-language utterances may occur. Preserve names and short command phrases.";
    }

    internal static string? GetSingleOpenAiLanguageCode(IReadOnlyList<VoiceLanguagePreference> languages)
    {
        if (languages.Count != 1)
        {
            return null;
        }

        return languages[0].OpenAiLanguageCode;
    }

    private static IReadOnlyList<VoiceLanguagePreference> GetDefaultLanguages()
        => [AmericanEnglish, Korean];

    private static VoiceLanguagePreference ParseSingle(string value)
    {
        var trimmed = value.Trim();
        var normalized = Normalize(trimmed);
        return normalized switch
        {
            "americanenglish" or "usenglish" or "unitedstatesenglish" or "englishus" or "enus" or "english" or "en" => AmericanEnglish,
            "korean" or "ko" or "kokr" or "koreanlanguage" => Korean,
            _ => new VoiceLanguagePreference(trimmed, TryExtractLanguageCode(trimmed))
        };
    }

    private static string GetDistinctKey(VoiceLanguagePreference language)
        => !string.IsNullOrWhiteSpace(language.OpenAiLanguageCode)
            ? language.OpenAiLanguageCode
            : Normalize(language.DisplayName);

    private static string Normalize(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }

            buffer[length] = char.ToLowerInvariant(character);
            length += 1;
        }

        return new string(buffer, 0, length);
    }

    private static string? TryExtractLanguageCode(string value)
    {
        var firstSegment = value
            .Trim()
            .Split(['-', '_'], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return null;
        }

        var letters = new string(firstSegment.Where(char.IsLetter).ToArray());
        return letters.Length is >= 2 and <= 3
            ? letters.ToLowerInvariant()
            : null;
    }
}
