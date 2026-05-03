using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HeronWin.Brain;

internal sealed class OpenAiCodexCliClient(
    string cliCommand,
    string model) : ILlmClient
{
    public LlmProviderId ProviderId => LlmProviderId.OpenAiCodex;
    public string DisplayName => string.IsNullOrWhiteSpace(model)
        ? "ChatGPT / Codex sign-in"
        : $"ChatGPT / Codex sign-in ({model})";
    public LlmModelProfile ModelProfile { get; } =
        LlmModelProfiles.Create(LlmProviderId.OpenAiCodex, string.IsNullOrWhiteSpace(model) ? "codex-default" : model);

    public async Task<ChatResult> ChatAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? systemPrompt,
        CancellationToken cancellationToken)
    {
        var bridgeRequest = CodexBridgeRequest.Create(messages, tools, systemPrompt);
        var schemaPath = CodexCliArtifacts.EnsureSchemaFile();
        var outputPath = Path.Combine(Path.GetTempPath(), $"brain-codex-output-{Guid.NewGuid():N}.json");
        var imageDirectory = Path.Combine(Path.GetTempPath(), $"brain-codex-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(imageDirectory);

        var imagePaths = new List<string>();
        try
        {
            foreach (var attachment in bridgeRequest.ImageAttachments)
            {
                var targetPath = Path.Combine(imageDirectory, attachment.FileName);
                await File.WriteAllBytesAsync(targetPath, attachment.Bytes, cancellationToken);
                imagePaths.Add(targetPath);
            }

            var processOutput = await RunCodexAsync(
                bridgeRequest.Prompt,
                schemaPath,
                outputPath,
                imagePaths,
                cancellationToken);

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Codex CLI did not produce a response file. {processOutput}");
            }

            var rawResponse = await File.ReadAllTextAsync(outputPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                throw new InvalidOperationException(
                    $"Codex CLI returned an empty response. {processOutput}");
            }

            using var document = JsonDocument.Parse(rawResponse);
            var root = document.RootElement;
            var text = root.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            var toolCalls = new List<ToolCallRequest>();
            if (root.TryGetProperty("toolCalls", out var toolCallsElement) &&
                toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolCall in toolCallsElement.EnumerateArray())
                {
                    var name = toolCall.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var arguments = toolCall.TryGetProperty("argumentsJson", out var argumentsElement)
                        ? argumentsElement.GetString() ?? "{}"
                        : "{}";
                    toolCalls.Add(new ToolCallRequest(
                        Guid.NewGuid().ToString("n"),
                        name,
                        arguments));
                }
            }

            return new ChatResult(
                string.IsNullOrWhiteSpace(text) ? null : text,
                toolCalls);
        }
        finally
        {
            TryDeleteFile(outputPath);
            TryDeleteDirectory(imageDirectory);
        }
    }

    private async Task<string> RunCodexAsync(
        string prompt,
        string schemaPath,
        string outputPath,
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken)
    {
        DebugTrace.WriteStructuredEvent(
            "llm.codex_cli.start",
            new Dictionary<string, object?>
            {
                ["provider"] = "openai-codex",
                ["command"] = cliCommand,
                ["model"] = string.IsNullOrWhiteSpace(model) ? "(default)" : model,
                ["images"] = imagePaths.Count,
            });

        using var process = OpenAiCodexCliSupport.StartProcess(
            cliCommand,
            Directory.GetCurrentDirectory(),
            args =>
            {
                args.Add("exec");
                args.Add("--sandbox");
                args.Add("read-only");
                args.Add("--color");
                args.Add("never");
                args.Add("--output-schema");
                args.Add(schemaPath);
                args.Add("--output-last-message");
                args.Add(outputPath);
                if (!string.IsNullOrWhiteSpace(model))
                {
                    args.Add("--model");
                    args.Add(model);
                }

                foreach (var imagePath in imagePaths)
                {
                    args.Add("--image");
                    args.Add(imagePath);
                }

                args.Add("-");
            });

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore shutdown failures.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var processOutput = OpenAiCodexCliSupport.SummarizeOutput(stdout, stderr);

        DebugTrace.WriteStructuredEvent(
            "llm.codex_cli.complete",
            new Dictionary<string, object?>
            {
                ["provider"] = "openai-codex",
                ["command"] = cliCommand,
                ["model"] = string.IsNullOrWhiteSpace(model) ? "(default)" : model,
                ["exitCode"] = process.ExitCode,
                ["outputPreview"] = DebugTrace.Preview(processOutput, 1200),
            });

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Codex CLI request failed with exit code {process.ExitCode}. {processOutput}");
        }

        return processOutput;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}

internal sealed record OpenAiCodexLoginStatus(
    bool IsAvailable,
    bool IsLoggedIn,
    string Message);

internal static class OpenAiCodexCliSupport
{
    public static OpenAiCodexLoginStatus GetLoginStatus(string cliCommand, string workingDirectory)
    {
        try
        {
            using var process = StartProcess(
                cliCommand,
                workingDirectory,
                args =>
                {
                    args.Add("login");
                    args.Add("status");
                });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.StandardInput.Close();
            process.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            var output = SummarizeOutput(stdout, stderr);
            var normalizedOutput = output.Trim();

            if (normalizedOutput.Contains("Logged in", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenAiCodexLoginStatus(true, true, normalizedOutput);
            }

            if (normalizedOutput.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                normalizedOutput.Contains("login required", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenAiCodexLoginStatus(true, false, normalizedOutput);
            }

            return process.ExitCode == 0
                ? new OpenAiCodexLoginStatus(true, false, string.IsNullOrWhiteSpace(normalizedOutput)
                    ? "Codex CLI is available, but login status is unclear."
                    : normalizedOutput)
                : new OpenAiCodexLoginStatus(true, false, string.IsNullOrWhiteSpace(normalizedOutput)
                    ? "Codex CLI login status check failed."
                    : normalizedOutput);
        }
        catch (Win32Exception)
        {
            return new OpenAiCodexLoginStatus(
                false,
                false,
                $"Could not start \"{cliCommand}\". Install Codex CLI or set OPENAI_CODEX_COMMAND to a valid executable.");
        }
        catch (Exception ex)
        {
            return new OpenAiCodexLoginStatus(
                false,
                false,
                $"Codex CLI login status check failed: {ex.Message}");
        }
    }

    public static Process StartProcess(
        string cliCommand,
        string workingDirectory,
        Action<Collection<string>> configureArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cliCommand,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        configureArguments(startInfo.ArgumentList);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException($"Could not start \"{cliCommand}\".");
    }

    public static string SummarizeOutput(string stdout, string stderr)
    {
        var combined = string.Join(
            "\n",
            new[] { stdout, stderr }
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Select(static text => text.Trim()));

        if (string.IsNullOrWhiteSpace(combined))
        {
            return "No Codex CLI output was captured.";
        }

        var lines = combined
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line =>
                !line.Equals("Reading additional input from stdin...", StringComparison.OrdinalIgnoreCase))
            .Take(16)
            .ToArray();
        return string.Join(" ", lines);
    }
}

internal static class CodexCliArtifacts
{
    private static readonly string SchemaPath = Path.Combine(Path.GetTempPath(), "brain-codex-bridge-schema.json");
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string EnsureSchemaFile()
    {
        File.WriteAllText(SchemaPath, SchemaJson, Utf8WithoutBom);
        return SchemaPath;
    }

    private const string SchemaJson =
        """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "text": { "type": "string" },
            "toolCalls": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "argumentsJson": { "type": "string" }
                },
                "required": ["name", "argumentsJson"],
                "additionalProperties": false
              }
            }
          },
          "required": ["text", "toolCalls"],
          "additionalProperties": false
        }
        """;
}

internal sealed record CodexImageAttachment(string FileName, byte[] Bytes);

internal sealed record CodexBridgeRequest(
    string Prompt,
    IReadOnlyList<CodexImageAttachment> ImageAttachments)
{
    public static CodexBridgeRequest Create(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? systemPrompt)
    {
        var imageAttachments = new List<CodexImageAttachment>();
        var conversation = new List<object>(messages.Count);

        for (var index = 0; index < messages.Count; index += 1)
        {
            switch (messages[index])
            {
                case AgentMessage.User user:
                    conversation.Add(new
                    {
                        role = "user",
                        content = user.Content
                    });
                    break;

                case AgentMessage.Summary summary:
                    conversation.Add(new
                    {
                        role = "summary",
                        content = summary.Content
                    });
                    break;

                case AgentMessage.VisualContext visualContext:
                    var imageFileNames = new List<string>(visualContext.Images.Count);
                    for (var imageIndex = 0; imageIndex < visualContext.Images.Count; imageIndex += 1)
                    {
                        var fileName = $"message-{index + 1:D3}-image-{imageIndex + 1:D2}{GetFileExtension(visualContext.Images[imageIndex].MimeType)}";
                        imageAttachments.Add(new CodexImageAttachment(
                            fileName,
                            Convert.FromBase64String(visualContext.Images[imageIndex].Base64Data)));
                        imageFileNames.Add(fileName);
                    }

                    conversation.Add(new
                    {
                        role = "user_visual",
                        content = visualContext.Content,
                        images = imageFileNames
                    });
                    break;

                case AgentMessage.Assistant assistant:
                    conversation.Add(new
                    {
                        role = "assistant",
                        content = assistant.Content ?? string.Empty,
                        toolCalls = assistant.ToolCalls?.Select(toolCall => new
                        {
                            id = toolCall.Id,
                            name = toolCall.Name,
                            arguments = DeserializeToolArguments(toolCall.Arguments)
                        }).ToArray() ?? []
                    });
                    break;

                case AgentMessage.ToolResult toolResult:
                    conversation.Add(new
                    {
                        role = "tool_result",
                        toolCallId = toolResult.ToolCallId,
                        toolName = toolResult.ToolName,
                        content = toolResult.Content
                    });
                    break;
            }
        }

        var envelope = new
        {
            instructions = new[]
            {
                "You are acting only as an LLM backend for another runtime.",
                "Do not use Codex tools, shell commands, the filesystem, plugins, MCP, or web search.",
                "Reason only over the provided system prompt, conversation history, and tool definitions.",
                "Return only a JSON object that matches the requested schema.",
                "When tools are needed, populate toolCalls with one or more tool requests whose arguments are JSON objects.",
                "Serialize each tool's arguments object into the argumentsJson string field as compact valid JSON.",
                "When no tools are needed, return the direct assistant output string in text exactly as the upstream runtime should receive it.",
                "If you include toolCalls, text may be empty or may contain a short assistant thought that should accompany the tool requests."
            },
            systemPrompt = systemPrompt ?? string.Empty,
            availableTools = tools.Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters
            }).ToArray(),
            conversation
        };

        var prompt = $$"""
        Follow the backend-bridge instructions exactly.
        Return only a schema-compliant JSON object with this shape:
        {
          "text": "assistant response string or empty string",
          "toolCalls": [
            {
              "name": "tool_name",
              "argumentsJson": "{\"json\":\"object\"}"
            }
          ]
        }

        Backend payload:
        {{JsonSerializer.Serialize(envelope, JsonSerializerOptionsCache.Default)}}
        """;

        return new CodexBridgeRequest(prompt, imageAttachments);
    }

    private static object DeserializeToolArguments(string rawArguments)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(rawArguments, JsonSerializerOptionsCache.Default)
                   ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string GetFileExtension(string mimeType)
        => mimeType.Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".bin"
        };
}
