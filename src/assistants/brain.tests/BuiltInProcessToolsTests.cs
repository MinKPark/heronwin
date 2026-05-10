using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace HeronWin.Brain.Tests;

public sealed class BuiltInProcessToolsTests
{
    [Fact]
    public void ParseStartProcessArguments_RequiresNonEmptyCommand()
    {
        AssertInvalidStartArguments(new Dictionary<string, object?>());
        AssertInvalidStartArguments(new Dictionary<string, object?> { ["command"] = null });
        AssertInvalidStartArguments(new Dictionary<string, object?> { ["command"] = string.Empty });
        AssertInvalidStartArguments(new Dictionary<string, object?> { ["command"] = "   " });
    }

    [Fact]
    public void ParseStartProcessArguments_AcceptsStringArrayArguments()
    {
        var parsed = BuiltInProcessTools.ParseStartProcessArguments(
            new Dictionary<string, object?>
            {
                ["command"] = "tool",
                ["args"] = new[] { "one", "two" }
            });

        Assert.Equal("tool", parsed.Command);
        Assert.Equal(["one", "two"], parsed.Args);
        Assert.Null(parsed.WorkingDirectory);
    }

    [Fact]
    public void ParseStartProcessArguments_AcceptsJsonArrayArguments()
    {
        var parsed = BuiltInProcessTools.ParseStartProcessArguments(
            new Dictionary<string, object?>
            {
                ["command"] = "tool",
                ["args"] = JsonElementFrom("""["one","two"]""")
            });

        Assert.Equal(["one", "two"], parsed.Args);
    }

    [Fact]
    public void ParseStartProcessArguments_RejectsNonStringArguments()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BuiltInProcessTools.ParseStartProcessArguments(
                new Dictionary<string, object?>
                {
                    ["command"] = "tool",
                    ["args"] = JsonElementFrom("""["ok", 1]""")
                }));

        Assert.Contains("must contain only strings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartProcess_ReturnsToolError_ForMissingWorkingDirectory()
    {
        var missingDirectory = Path.Combine(
            Path.GetTempPath(),
            "heronwin-missing-process-tool-cwd",
            Guid.NewGuid().ToString("N"));

        var result = await BuiltInProcessTools.CallAsync(
            "start_process",
            new Dictionary<string, object?>
            {
                ["command"] = "tool",
                ["cwd"] = missingDirectory
            },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Working directory not found", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseStopProcessArguments_RequiresPositivePid()
    {
        AssertInvalidStopArguments(new Dictionary<string, object?>());
        AssertInvalidStopArguments(new Dictionary<string, object?> { ["pid"] = 0 });
        AssertInvalidStopArguments(new Dictionary<string, object?> { ["pid"] = -1 });
        AssertInvalidStopArguments(new Dictionary<string, object?> { ["pid"] = 1.5d });
        AssertInvalidStopArguments(new Dictionary<string, object?> { ["pid"] = (long)int.MaxValue + 1 });
    }

    [Fact]
    public void ParseStopProcessArguments_AcceptsJsonNumberPid()
    {
        var parsed = BuiltInProcessTools.ParseStopProcessArguments(
            new Dictionary<string, object?> { ["pid"] = JsonElementFrom("123") });

        Assert.Equal(123, parsed.Pid);
        Assert.False(parsed.Force);
    }

    [Fact]
    public void ParseStopProcessArguments_AcceptsJsonBooleanForce()
    {
        var parsed = BuiltInProcessTools.ParseStopProcessArguments(
            new Dictionary<string, object?>
            {
                ["pid"] = JsonElementFrom("123"),
                ["force"] = JsonElementFrom("true")
            });

        Assert.Equal(123, parsed.Pid);
        Assert.True(parsed.Force);
    }

    [Fact]
    public void ParseStopProcessArguments_RejectsNonBooleanForce()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BuiltInProcessTools.ParseStopProcessArguments(
                new Dictionary<string, object?>
                {
                    ["pid"] = 123,
                    ["force"] = JsonElementFrom("\"yes\"")
                }));

        Assert.Contains("must be a boolean", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseWindowsTaskListCsv_HandlesQuotedNamesAndMemoryFields()
    {
        var processes = BuiltInProcessTools.ParseWindowsTaskListCsv(
            """
            "System Idle Process","0","Services","0","8 K"
            "comma, app.exe","1234","Console","1","12,344 K"
            "quote ""app"".exe","5678","Console","1","1,024 K"
            """);

        Assert.Equal(3, processes.Count);
        Assert.Equal(0, processes[0].Pid);
        Assert.Equal("System Idle Process", processes[0].Name);
        Assert.Equal("Services", processes[0].SessionName);
        Assert.Equal(1234, processes[1].Pid);
        Assert.Equal("comma, app.exe", processes[1].Name);
        Assert.Equal("quote \"app\".exe", processes[2].Name);
    }

    [Fact]
    public void ParseWindowsTaskListCsv_SkipsMalformedRowsWithoutThrowing()
    {
        var processes = BuiltInProcessTools.ParseWindowsTaskListCsv(
            """
            "broken.exe","not-a-pid","Console","1","1 K"
            "good.exe","222","Console","1","2 K"
            """);

        var process = Assert.Single(processes);
        Assert.Equal(222, process.Pid);
        Assert.Equal("good.exe", process.Name);
    }

    [Fact]
    public void ParsePsAux_HandlesWhitespaceAndCommandWithSpaces()
    {
        var processes = BuiltInProcessTools.ParsePsAux(
            """
            USER       PID %CPU %MEM    VSZ   RSS TTY      STAT START   TIME COMMAND
            neo        234  0.0  0.1 123456   789 ?        Ssl  10:00   0:01 /usr/bin/python script.py --flag value
            """);

        var process = Assert.Single(processes);
        Assert.Equal(234, process.Pid);
        Assert.Equal("neo", process.User);
        Assert.Equal("/usr/bin/python script.py --flag value", process.Name);
    }

    [Fact]
    public void FormatProcessList_IncludesNameAndPid()
    {
        var formatted = BuiltInProcessTools.FormatProcessList(
        [
            new ProcessSummary(123, "app.exe", "Console", null, null)
        ]);

        Assert.Contains("PID\tName\tSession\tUser", formatted, StringComparison.Ordinal);
        Assert.Contains("123\tapp.exe\tConsole\t", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopProcess_ReturnsToolError_ForUnknownPid()
    {
        var result = await BuiltInProcessTools.CallAsync(
            "stop_process",
            new Dictionary<string, object?> { ["pid"] = int.MaxValue },
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Failed to stop process", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartListStopProcess_HandlesTestOwnedProcess()
    {
        int? pid = null;
        Process? process = null;
        try
        {
            var startResult = await BuiltInProcessTools.CallAsync(
                "start_process",
                new Dictionary<string, object?>
                {
                    ["command"] = GetWindowsPowerShellPath(),
                    ["args"] = new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }
                },
                CancellationToken.None);

            Assert.False(startResult.IsError, startResult.Text);
            pid = ExtractPid(startResult.Text);
            process = Process.GetProcessById(pid.Value);
            Assert.False(process.HasExited);

            var listResult = await BuiltInProcessTools.CallAsync(
                "list_processes",
                new Dictionary<string, object?>(),
                CancellationToken.None);

            Assert.False(listResult.IsError, listResult.Text);
            Assert.Contains(pid.Value.ToString(CultureInfo.InvariantCulture), listResult.Text, StringComparison.Ordinal);

            var stopResult = await BuiltInProcessTools.CallAsync(
                "stop_process",
                new Dictionary<string, object?>
                {
                    ["pid"] = pid.Value,
                    ["force"] = true
                },
                CancellationToken.None);

            Assert.False(stopResult.IsError, stopResult.Text);
            Assert.True(
                await WaitForExitAsync(process, TimeSpan.FromSeconds(5)),
                $"Process {pid.Value} did not exit after stop_process.");
        }
        finally
        {
            await KillTestProcessIfRunningAsync(process);
            process?.Dispose();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartProcess_UsesProvidedWorkingDirectory()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "HeronWin.Brain.Tests",
            Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(tempDirectory, "cwd.txt");
        int? pid = null;
        Process? process = null;

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var startResult = await BuiltInProcessTools.CallAsync(
                "start_process",
                new Dictionary<string, object?>
                {
                    ["command"] = GetWindowsPowerShellPath(),
                    ["args"] = new[]
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        "Set-Content -LiteralPath '.\\cwd.txt' -Value (Get-Location).Path; Start-Sleep -Seconds 30"
                    },
                    ["cwd"] = tempDirectory
                },
                CancellationToken.None);

            Assert.False(startResult.IsError, startResult.Text);
            pid = ExtractPid(startResult.Text);
            process = Process.GetProcessById(pid.Value);
            Assert.True(
                await WaitForFileAsync(markerPath, TimeSpan.FromSeconds(5)),
                $"Expected marker file was not created at {markerPath}.");

            var actualDirectory = (await File.ReadAllTextAsync(markerPath)).Trim();
            Assert.True(
                string.Equals(NormalizePath(tempDirectory), NormalizePath(actualDirectory), StringComparison.OrdinalIgnoreCase),
                $"Expected working directory {tempDirectory}, got {actualDirectory}.");
        }
        finally
        {
            await KillTestProcessIfRunningAsync(process);
            process?.Dispose();
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static void AssertInvalidStartArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BuiltInProcessTools.ParseStartProcessArguments(arguments));
        Assert.Contains("command", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertInvalidStopArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BuiltInProcessTools.ParseStopProcessArguments(arguments));
        Assert.Contains("pid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement JsonElementFrom(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string GetWindowsPowerShellPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            var fullPath = Path.Combine(
                windowsDirectory,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return "powershell.exe";
    }

    private static int ExtractPid(string text)
    {
        var match = Regex.Match(text, @"PID:\s*(\d+)", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Could not find PID in tool output: {text}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return File.Exists(path);
    }

    private static async Task KillTestProcessIfRunningAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
        }
    }
}
