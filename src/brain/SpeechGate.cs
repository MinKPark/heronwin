using System.Text;
using System.Text.RegularExpressions;

namespace HeronWin.Brain;

internal static class SpeechGate
{
    public static bool ContainsWakeWord(string text, string wakeWord)
    {
        var normalizedText = Normalize(text);
        var normalizedWakeWord = Normalize(wakeWord);
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedWakeWord))
        {
            return false;
        }

        if (ContainsWholePhrase(normalizedText, normalizedWakeWord))
        {
            return true;
        }

        var wakeWordTokens = normalizedWakeWord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var transcriptTokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wakeWordTokens.Length < 2 || transcriptTokens.Length < wakeWordTokens.Length)
        {
            return false;
        }

        for (var start = 0; start <= transcriptTokens.Length - wakeWordTokens.Length; start += 1)
        {
            var candidateTokens = transcriptTokens
                .Skip(start)
                .Take(wakeWordTokens.Length)
                .ToArray();

            if (!string.Equals(candidateTokens[0], wakeWordTokens[0], StringComparison.Ordinal))
            {
                continue;
            }

            var candidatePhrase = string.Join(" ", candidateTokens);
            if (IsFuzzyPhraseMatch(candidatePhrase, normalizedWakeWord))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ShouldExitApp(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized == "bye"
               || normalized == "bye bye"
               || normalized.EndsWith(" bye", StringComparison.Ordinal)
               || normalized.EndsWith(" bye bye", StringComparison.Ordinal);
    }

    private static bool ContainsWholePhrase(string normalizedText, string normalizedPhrase)
    {
        var paddedText = $" {normalizedText} ";
        var paddedPhrase = $" {normalizedPhrase} ";
        return paddedText.Contains(paddedPhrase, StringComparison.Ordinal);
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(text, @"[^\p{L}\p{N}\s-]", " ")
            .ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[-\s]+", " ").Trim();
        return normalized;
    }

    private static bool IsFuzzyPhraseMatch(string candidate, string wakeWord)
    {
        var maxLength = Math.Max(candidate.Length, wakeWord.Length);
        if (maxLength == 0)
        {
            return false;
        }

        var distance = ComputeLevenshteinDistance(candidate, wakeWord);
        var allowedDistance = Math.Max(1, (int)Math.Round(maxLength * 0.30, MidpointRounding.AwayFromZero));
        return distance <= allowedDistance;
    }

    private static int ComputeLevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previousRow = new int[right.Length + 1];
        var currentRow = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column += 1)
        {
            previousRow[column] = column;
        }

        for (var row = 0; row < left.Length; row += 1)
        {
            currentRow[0] = row + 1;
            for (var column = 0; column < right.Length; column += 1)
            {
                var substitutionCost = left[row] == right[column] ? 0 : 1;
                currentRow[column + 1] = Math.Min(
                    Math.Min(
                        currentRow[column] + 1,
                        previousRow[column + 1] + 1),
                    previousRow[column] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length];
    }
}
