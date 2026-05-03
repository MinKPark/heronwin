using System.Diagnostics;
using System.Text.Json;

namespace HeronWin.Brain;

internal static class BuiltInProcessTools
{
    private static readonly IReadOnlyDictionary<string, ToolDefinition> DefinitionsByName =
        new Dictionary<string, ToolDefinition>(StringComparer.Ordinal)
        {
            ["list_processes"] = CreateToolDefinition(
                "list_processes",
                "List running processes on the local machine",
                """
                {
                  "type": "object",
                  "properties": {},
                  "additionalProperties": false
                }
                """),
            ["start_process"] = CreateToolDefinition(
                "start_process",
                "Start a new process on the local machine",
                """
                {
                  "type": "object",
                  "properties": {
                    "command": {
                      "type": "string",
                      "description": "The executable command to run"
                    },
                    "args": {
                      "type": "array",
                      "items": { "type": "string" },
                      "description": "Command-line arguments for the process"
                    },
                    "cwd": {
                      "type": "string",
                      "description": "Working directory for the process"
                    }
                  },
                  "required": ["command"],
                  "additionalProperties": false
                }
                """),
            ["stop_process"] = CreateToolDefinition(
                "stop_process",
                "Stop a running process by its PID",
                """
                {
                  "type": "object",
                  "properties": {
                    "pid": {
                      "type": "integer",
                      "minimum": 1,
                      "description": "Process ID (PID) of the process to stop"
                    },
                    "force": {
                      "type": "boolean",
                      "description": "Force-kill the process and its child processes when supported"
                    }
                  },
                  "required": ["pid"],
                  "additionalProperties": false
                }
                """)
        };

    public static IReadOnlyList<ToolDefinition> Definitions { get; } = DefinitionsByName.Values.ToArray();

    public static bool CanHandle(string toolName)
        => DefinitionsByName.ContainsKey(toolName);

    public static async Task<ToolCallOutcome> CallAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
        => toolName switch
        {
            "list_processes" => await ListProcessesAsync(cancellationToken),
            "start_process" => StartProcess(arguments),
            "stop_process" => StopProcess(arguments),
            _ => new ToolCallOutcome($"Built-in tool \"{toolName}\" was not found.", [], true)
        };

    private static async Task<ToolCallOutcome> ListProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var output = OperatingSystem.IsWindows()
                ? await RunForOutputAsync("tasklist", ["/FO", "CSV", "/NH"], cancellationToken)
                : await RunForOutputAsync("ps", ["aux"], cancellationToken);

            return new ToolCallOutcome(output.Trim(), []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ToolCallOutcome($"Failed to list processes: {ex.Message}", [], true);
        }
    }

    private static ToolCallOutcome StartProcess(IReadOnlyDictionary<string, object?> arguments)
    {
        try
        {
            var command = GetRequiredString(arguments, "command");
            var args = GetOptionalStringArray(arguments, "args");
            var cwd = GetOptionalString(arguments, "cwd");

            if (!string.IsNullOrWhiteSpace(cwd) && !Directory.Exists(cwd))
            {
                throw new DirectoryNotFoundException($"Working directory not found: {cwd}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (!string.IsNullOrWhiteSpace(cwd))
            {
                startInfo.WorkingDirectory = cwd;
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ToolCallOutcome("Failed to start process: no process handle was returned", [], true);
            }

            var pid = process.Id;
            _ = DrainAndDisposeAsync(process);

            return new ToolCallOutcome($"Process started successfully with PID: {pid}", []);
        }
        catch (Exception ex)
        {
            return new ToolCallOutcome($"Failed to start process: {ex.Message}", [], true);
        }
    }

    private static ToolCallOutcome StopProcess(IReadOnlyDictionary<string, object?> arguments)
    {
        var pidText = "(unknown)";
        try
        {
            var pid = GetRequiredPositiveInt(arguments, "pid");
            pidText = pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var force = GetOptionalBool(arguments, "force") ?? false;

            using var process = Process.GetProcessById(pid);
            if (!force && process.CloseMainWindow())
            {
                return new ToolCallOutcome($"Sent close request to process {pid}", []);
            }

            process.Kill(entireProcessTree: force);
            return new ToolCallOutcome(
                force
                    ? $"Killed process {pid} and its child processes"
                    : $"Stopped process {pid}",
                []);
        }
        catch (Exception ex)
        {
            return new ToolCallOutcome($"Failed to stop process {pidText}: {ex.Message}", [], true);
        }
    }

    private static async Task<string> RunForOutputAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("No process handle was returned.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(stderr)
                ? $"{command} exited with code {process.ExitCode}."
                : stderr.Trim();
            throw new InvalidOperationException(message);
        }

        return stdout;
    }

    private static async Task DrainAndDisposeAsync(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch
        {
            // Best effort only; some process types close standard input first.
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch
        {
            // The spawned process is intentionally detached from the agent flow.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, object?> arguments, string name)
    {
        var value = GetOptionalString(arguments, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Required argument \"{name}\" must be a non-empty string.");
        }

        return value;
    }

    private static string? GetOptionalString(IReadOnlyDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => throw new ArgumentException($"Argument \"{name}\" must be a string.")
        };
    }

    private static IReadOnlyList<string> GetOptionalStringArray(
        IReadOnlyDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }

        if (value is string[] strings)
        {
            return strings;
        }

        if (value is IEnumerable<string> enumerable)
        {
            return enumerable.ToArray();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            var result = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException($"Argument \"{name}\" must contain only strings.");
                }

                result.Add(item.GetString() ?? string.Empty);
            }

            return result;
        }

        throw new ArgumentException($"Argument \"{name}\" must be an array of strings.");
    }

    private static int GetRequiredPositiveInt(IReadOnlyDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            throw new ArgumentException($"Required argument \"{name}\" must be a positive integer.");
        }

        var parsed = value switch
        {
            int intValue => intValue,
            long longValue when longValue <= int.MaxValue => (int)longValue,
            double doubleValue when doubleValue % 1 == 0 && doubleValue <= int.MaxValue => (int)doubleValue,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var intValue) => intValue,
            _ => throw new ArgumentException($"Argument \"{name}\" must be a positive integer.")
        };

        if (parsed <= 0)
        {
            throw new ArgumentException($"Argument \"{name}\" must be a positive integer.");
        }

        return parsed;
    }

    private static bool? GetOptionalBool(IReadOnlyDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool boolValue => boolValue,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => throw new ArgumentException($"Argument \"{name}\" must be a boolean.")
        };
    }

    private static ToolDefinition CreateToolDefinition(string name, string description, string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return new ToolDefinition(name, description, document.RootElement.Clone());
    }
}
