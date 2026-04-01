using HeronWin.HerBody.EyesAndHands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

DebugTrace.Configure(args);

if (await ConsoleMode.TryRunAsync(args))
{
    return;
}

var filteredArgs = args
    .Where(arg => !string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase))
    .ToArray();

var builder = Host.CreateApplicationBuilder(filteredArgs);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    if (DebugTrace.IsEnabled)
    {
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
    }
});

builder.Services.AddSingleton<WindowSelectionState>();
builder.Services.AddSingleton<UiAutomationExecutor>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
