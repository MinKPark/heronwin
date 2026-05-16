namespace HeronWin.Brain;

internal sealed record OpenAiCodexModelInfo(
    string RequestedModel,
    string EffectiveModel,
    string? CliModel,
    bool IsSpark,
    bool SupportsImageInputs)
{
    public bool UsesDefaultModel => string.IsNullOrWhiteSpace(CliModel);
    public string TraceModel => UsesDefaultModel ? "(default)" : EffectiveModel;
}

internal static class OpenAiCodexModels
{
    public const string DefaultModelName = "codex-default";
    public const string SparkModelName = "gpt-5.3-codex-spark";

    public static string NormalizeConfiguredModel(string? model)
    {
        var modelInfo = Resolve(model);
        return modelInfo.UsesDefaultModel ? string.Empty : modelInfo.EffectiveModel;
    }

    public static OpenAiCodexModelInfo Resolve(string? model)
    {
        var requestedModel = model?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            return new OpenAiCodexModelInfo(
                requestedModel,
                DefaultModelName,
                CliModel: null,
                IsSpark: false,
                SupportsImageInputs: true);
        }

        if (IsSparkAlias(requestedModel))
        {
            return new OpenAiCodexModelInfo(
                requestedModel,
                SparkModelName,
                SparkModelName,
                IsSpark: true,
                SupportsImageInputs: false);
        }

        return new OpenAiCodexModelInfo(
            requestedModel,
            requestedModel,
            requestedModel,
            IsSpark: false,
            SupportsImageInputs: true);
    }

    private static bool IsSparkAlias(string model)
        => model.Trim().ToLowerInvariant() is "spark" or "codex-spark" or SparkModelName;
}
