namespace HeronWin.Brain;

internal sealed record LlmModelProfile(
    LlmProviderId ProviderId,
    string ModelName,
    double ContextCompressionTriggerRatio,
    int WindowSnapshotCharBudget,
    int FocusSnapshotCharBudget,
    int MaxThrottleRetries);

internal static class LlmModelProfiles
{
    public static LlmModelProfile Create(LlmProviderId providerId, string modelName)
    {
        var normalizedModel = string.IsNullOrWhiteSpace(modelName)
            ? "(unknown)"
            : modelName.Trim();

        return providerId switch
        {
            LlmProviderId.OpenAiApi => CreateOpenAiProfile(normalizedModel),
            LlmProviderId.ClaudeApi => CreateClaudeProfile(normalizedModel),
            _ => CreateFallbackProfile(providerId, normalizedModel)
        };
    }

    private static LlmModelProfile CreateOpenAiProfile(string modelName)
    {
        if (modelName.Contains("mini", StringComparison.OrdinalIgnoreCase))
        {
            return new LlmModelProfile(
                LlmProviderId.OpenAiApi,
                modelName,
                ContextCompressionTriggerRatio: 0.55,
                WindowSnapshotCharBudget: 4_800,
                FocusSnapshotCharBudget: 2_800,
                MaxThrottleRetries: 2);
        }

        if (modelName.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
        {
            return new LlmModelProfile(
                LlmProviderId.OpenAiApi,
                modelName,
                ContextCompressionTriggerRatio: 0.62,
                WindowSnapshotCharBudget: 7_200,
                FocusSnapshotCharBudget: 3_600,
                MaxThrottleRetries: 2);
        }

        return new LlmModelProfile(
            LlmProviderId.OpenAiApi,
            modelName,
            ContextCompressionTriggerRatio: 0.65,
            WindowSnapshotCharBudget: 8_000,
            FocusSnapshotCharBudget: 4_000,
            MaxThrottleRetries: 2);
    }

    private static LlmModelProfile CreateClaudeProfile(string modelName)
    {
        if (modelName.Contains("haiku", StringComparison.OrdinalIgnoreCase))
        {
            return new LlmModelProfile(
                LlmProviderId.ClaudeApi,
                modelName,
                ContextCompressionTriggerRatio: 0.60,
                WindowSnapshotCharBudget: 6_000,
                FocusSnapshotCharBudget: 3_200,
                MaxThrottleRetries: 2);
        }

        return new LlmModelProfile(
            LlmProviderId.ClaudeApi,
            modelName,
            ContextCompressionTriggerRatio: 0.68,
            WindowSnapshotCharBudget: 9_000,
            FocusSnapshotCharBudget: 4_500,
            MaxThrottleRetries: 2);
    }

    private static LlmModelProfile CreateFallbackProfile(LlmProviderId providerId, string modelName)
        => new(
            providerId,
            modelName,
            ContextCompressionTriggerRatio: 0.65,
            WindowSnapshotCharBudget: 7_000,
            FocusSnapshotCharBudget: 3_500,
            MaxThrottleRetries: 2);
}
