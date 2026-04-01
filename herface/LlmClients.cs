using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeronWin.HerFace;

internal static class LlmFactory
{
    public static ILlmClient CreateLlmClient(AppConfig config, HttpClient httpClient)
        => config.LlmProvider switch
        {
            LlmProviderId.OpenAiApi when string.IsNullOrWhiteSpace(config.OpenAiApiKey) =>
                throw new InvalidOperationException(
                    "OPENAI_API_KEY is not set. OpenAI API mode requires an API key."),
            LlmProviderId.OpenAiApi => new OpenAiApiClient(
                httpClient,
                config.OpenAiApiKey,
                config.OpenAiModel,
                config.AgentDefinition,
                config.LlmTemperature),
            LlmProviderId.ClaudeApi when string.IsNullOrWhiteSpace(config.AnthropicApiKey) =>
                throw new InvalidOperationException(
                    "ANTHROPIC_API_KEY is not set. Claude API mode requires an API key."),
            LlmProviderId.ClaudeApi => new ClaudeApiClient(
                httpClient,
                config.AnthropicApiKey,
                config.AnthropicModel,
                config.AgentDefinition,
                config.LlmTemperature),
            _ => throw new InvalidOperationException("Unsupported LLM provider.")
        };

    public static IAudioTranscriber? CreateAudioTranscriber(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiWhisperTranscriber(httpClient, config.OpenAiApiKey, config.WhisperModel);

    public static ISpeechSynthesizer? CreateSpeechSynthesizer(AppConfig config, HttpClient httpClient)
        => string.IsNullOrWhiteSpace(config.OpenAiApiKey)
            ? null
            : new OpenAiSpeechSynthesizer(
                httpClient,
                config.OpenAiApiKey,
                config.TtsModel,
                config.TtsVoice,
                config.TtsInstructions);
}

internal interface ISpeechSynthesizer
{
    string DisplayName { get; }
    Task<string> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken);
}

internal sealed class OpenAiApiClient(
    HttpClient httpClient,
    string apiKey,
    string model,
    string agentDefinition,
    double temperature) : ILlmClient
{
    public LlmProviderId ProviderId => LlmProviderId.OpenAiApi;
    public string DisplayName => $"OpenAI API ({model})";

    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        DebugTrace.WriteEvent(
            "llm.http.start",
            $"provider=OpenAI, model={model}, messages={messages.Count}, tools={tools.Count}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = ToOpenAiMessages(messages, agentDefinition)
        };

        if (SupportsTemperatureControl(model))
        {
            payload["temperature"] = temperature;
        }

        if (tools.Count > 0)
        {
            payload["tools"] = ToOpenAiTools(tools);
            payload["tool_choice"] = "auto";
        }

        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugTrace.WriteEvent(
            "llm.http.complete",
            $"provider=OpenAI, model={model}, status={(int)response.StatusCode}, response={DebugTrace.Preview(responseText, 1200)}");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Chat request failed ({(int)response.StatusCode}): {ApiErrorParser.ExtractApiError(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");

        string? text = null;
        if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
        {
            text = contentElement.GetString();
        }

        var toolCalls = new List<ToolCallRequest>();
        if (message.TryGetProperty("tool_calls", out var toolCallArray) && toolCallArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in toolCallArray.EnumerateArray())
            {
                toolCalls.Add(new ToolCallRequest(
                    toolCall.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("n"),
                    toolCall.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                    toolCall.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"));
            }
        }

        return new ChatResult(text, toolCalls);
    }

    private static JsonArray ToOpenAiMessages(IReadOnlyList<AgentMessage> messages, string agentDefinition)
    {
        var result = new JsonArray();
        if (!string.IsNullOrWhiteSpace(agentDefinition))
        {
            result.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = agentDefinition
            });
        }

        foreach (var message in messages)
        {
            switch (message)
            {
                case AgentMessage.User user:
                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = user.Content
                    });
                    break;

                case AgentMessage.Summary summary:
                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = $"Conversation summary from earlier turns. Treat this as reference context, not a new request.\n{summary.Content}"
                    });
                    break;

                case AgentMessage.VisualContext visualContext:
                    var visionContent = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = visualContext.Content
                        }
                    };

                    foreach (var image in visualContext.Images)
                    {
                        visionContent.Add(new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = $"data:{image.MimeType};base64,{image.Base64Data}"
                            }
                        });
                    }

                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = visionContent
                    });
                    break;

                case AgentMessage.Assistant assistant when assistant.ToolCalls is { Count: > 0 }:
                    var toolCalls = new JsonArray();
                    foreach (var toolCall in assistant.ToolCalls)
                    {
                        toolCalls.Add(new JsonObject
                        {
                            ["id"] = toolCall.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = toolCall.Name,
                                ["arguments"] = toolCall.Arguments
                            }
                        });
                    }

                    result.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = assistant.Content is null ? null : JsonValue.Create(assistant.Content),
                        ["tool_calls"] = toolCalls
                    });
                    break;

                case AgentMessage.Assistant assistant:
                    result.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = assistant.Content ?? string.Empty
                    });
                    break;

                case AgentMessage.ToolResult toolResult:
                    result.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolResult.ToolCallId,
                        ["content"] = toolResult.Content
                    });
                    break;
            }
        }

        return result;
    }

    private static JsonArray ToOpenAiTools(IReadOnlyList<ToolDefinition> tools)
    {
        var result = new JsonArray();
        foreach (var tool in tools)
        {
            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText())
                }
            });
        }

        return result;
    }

    private static bool SupportsTemperatureControl(string model)
        => !model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
}

internal sealed class OpenAiWhisperTranscriber(HttpClient httpClient, string apiKey, string whisperModel) : IAudioTranscriber
{
    public string DisplayName => $"OpenAI Whisper ({whisperModel})";

    public async Task<string> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(audioFilePath);
        DebugTrace.WriteEvent(
            "audio.transcription.start",
            $"provider=OpenAI Whisper, model={whisperModel}, path={audioFilePath}, bytes={(fileInfo.Exists ? fileInfo.Length : 0)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(whisperModel), "model");
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(audioFilePath, cancellationToken));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "recording.wav");
        request.Content = content;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugTrace.WriteEvent(
            "audio.transcription.complete",
            $"provider=OpenAI Whisper, model={whisperModel}, status={(int)response.StatusCode}, response={DebugTrace.Preview(responseText, 1000)}");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Transcription request failed ({(int)response.StatusCode}): {ApiErrorParser.ExtractApiError(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        return document.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
    }
}

internal sealed class OpenAiSpeechSynthesizer(
    HttpClient httpClient,
    string apiKey,
    string ttsModel,
    string ttsVoice,
    string ttsInstructions) : ISpeechSynthesizer
{
    public string DisplayName => $"OpenAI TTS ({ttsModel}, {ttsVoice})";

    public async Task<string> SynthesizeSpeechAsync(string text, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"herface-tts-{Guid.NewGuid():N}.wav");
        DebugTrace.WriteEvent(
            "audio.tts.start",
            $"provider=OpenAI TTS, model={ttsModel}, voice={ttsVoice}, text={DebugTrace.Preview(text, 500)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new JsonObject
        {
            ["model"] = ttsModel,
            ["voice"] = ttsVoice,
            ["input"] = text,
            ["instructions"] = ttsInstructions,
            ["response_format"] = "wav"
        };

        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            DebugTrace.WriteEvent(
                "audio.tts.complete",
                $"provider=OpenAI TTS, model={ttsModel}, voice={ttsVoice}, status={(int)response.StatusCode}, response={DebugTrace.Preview(responseText, 1000)}");
            throw new InvalidOperationException(
                $"Speech synthesis request failed ({(int)response.StatusCode}): {ApiErrorParser.ExtractApiError(responseText)}");
        }

        await using var fileStream = File.Create(outputPath);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);
        DebugTrace.WriteEvent(
            "audio.tts.complete",
            $"provider=OpenAI TTS, model={ttsModel}, voice={ttsVoice}, status={(int)response.StatusCode}, outputPath={outputPath}");
        return outputPath;
    }
}

internal sealed class ClaudeApiClient(
    HttpClient httpClient,
    string apiKey,
    string model,
    string agentDefinition,
    double temperature) : ILlmClient
{
    public LlmProviderId ProviderId => LlmProviderId.ClaudeApi;
    public string DisplayName => $"Claude API ({model})";

    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        DebugTrace.WriteEvent(
            "llm.http.start",
            $"provider=Claude, model={model}, messages={messages.Count}, tools={tools.Count}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var payload = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = temperature,
            ["max_tokens"] = 4096,
            ["messages"] = ToAnthropicMessages(messages)
        };

        if (!string.IsNullOrWhiteSpace(agentDefinition))
        {
            payload["system"] = agentDefinition;
        }

        if (tools.Count > 0)
        {
            payload["tools"] = ToAnthropicTools(tools);
        }

        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        DebugTrace.WriteEvent(
            "llm.http.complete",
            $"provider=Claude, model={model}, status={(int)response.StatusCode}, response={DebugTrace.Preview(responseText, 1200)}");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude request failed ({(int)response.StatusCode}): {ApiErrorParser.ExtractApiError(responseText)}");
        }

        using var document = JsonDocument.Parse(responseText);
        string? text = null;
        var toolCalls = new List<ToolCallRequest>();

        foreach (var block in document.RootElement.GetProperty("content").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "text")
            {
                text = (text ?? string.Empty) + (block.GetProperty("text").GetString() ?? string.Empty);
            }
            else if (type == "tool_use")
            {
                toolCalls.Add(new ToolCallRequest(
                    block.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("n"),
                    block.GetProperty("name").GetString() ?? string.Empty,
                    block.GetProperty("input").GetRawText()));
            }
        }

        return new ChatResult(text, toolCalls);
    }

    private static JsonArray ToAnthropicMessages(IReadOnlyList<AgentMessage> messages)
    {
        var result = new JsonArray();
        var index = 0;
        while (index < messages.Count)
        {
            switch (messages[index])
            {
                case AgentMessage.User user:
                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = user.Content
                    });
                    index += 1;
                    break;

                case AgentMessage.Summary summary:
                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = $"Conversation summary from earlier turns. Treat this as reference context, not a new request.\n{summary.Content}"
                    });
                    index += 1;
                    break;

                case AgentMessage.VisualContext visualContext:
                    var visualContent = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = visualContext.Content
                        }
                    };

                    foreach (var image in visualContext.Images)
                    {
                        visualContent.Add(new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = image.MimeType,
                                ["data"] = image.Base64Data
                            }
                        });
                    }

                    result.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = visualContent
                    });
                    index += 1;
                    break;

                case AgentMessage.Assistant assistant when assistant.ToolCalls is { Count: > 0 }:
                    var assistantContent = new JsonArray();
                    if (!string.IsNullOrWhiteSpace(assistant.Content))
                    {
                        assistantContent.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = assistant.Content
                        });
                    }

                    foreach (var toolCall in assistant.ToolCalls)
                    {
                        assistantContent.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = toolCall.Id,
                            ["name"] = toolCall.Name,
                            ["input"] = JsonNode.Parse(toolCall.Arguments)
                        });
                    }

                    result.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = assistantContent
                    });

                    index += 1;
                    var toolResults = new JsonArray();
                    while (index < messages.Count && messages[index] is AgentMessage.ToolResult toolResult)
                    {
                        toolResults.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = toolResult.ToolCallId,
                            ["content"] = toolResult.Content
                        });
                        index += 1;
                    }

                    if (toolResults.Count > 0)
                    {
                        result.Add(new JsonObject
                        {
                            ["role"] = "user",
                            ["content"] = toolResults
                        });
                    }
                    break;

                case AgentMessage.Assistant assistant:
                    result.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = assistant.Content ?? string.Empty
                    });
                    index += 1;
                    break;

                default:
                    index += 1;
                    break;
            }
        }

        return result;
    }

    private static JsonArray ToAnthropicTools(IReadOnlyList<ToolDefinition> tools)
    {
        var result = new JsonArray();
        foreach (var tool in tools)
        {
            result.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonNode.Parse(tool.Parameters.GetRawText())
            });
        }

        return result;
    }
}

internal static class ApiErrorParser
{
    public static string ExtractApiError(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    return errorElement.GetString() ?? responseText;
                }

                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    return messageElement.GetString() ?? responseText;
                }
            }
        }
        catch
        {
            // Fall back to the raw response.
        }

        return responseText;
    }
}
