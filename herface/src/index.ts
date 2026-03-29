import readline from "readline";
import { loadConfig } from "./config.js";
import { OpenAILLMClient } from "./llm/openai.js";
import { ClaudeLLMClient } from "./llm/claude.js";
import { McpClientManager } from "./mcp/client.js";
import { recordAudio } from "./voice/recorder.js";
import { display } from "./ui/display.js";
import { runAgentTurn } from "./agent.js";
import type { LLMClient, AgentMessage } from "./llm/types.js";

async function main(): Promise<void> {
  display.banner();

  // ── Config ─────────────────────────────────────────────────
  const cfg = loadConfig();

  // ── LLM client ─────────────────────────────────────────────
  let llmClient: LLMClient;
  if (cfg.llmProvider === "openai") {
    if (!cfg.openaiApiKey) {
      display.error("OPENAI_API_KEY is not set. Copy .env.example to .env and fill it in.");
      process.exit(1);
    }
    llmClient = new OpenAILLMClient(cfg.openaiApiKey, cfg.openaiModel, cfg.whisperModel);
    display.info(`LLM: OpenAI ${cfg.openaiModel}`);
  } else {
    if (!cfg.anthropicApiKey) {
      display.error("ANTHROPIC_API_KEY is not set. Copy .env.example to .env and fill it in.");
      process.exit(1);
    }
    llmClient = new ClaudeLLMClient(cfg.anthropicApiKey, cfg.anthropicModel);
    display.info(`LLM: Anthropic ${cfg.anthropicModel}`);
  }

  // ── MCP servers ────────────────────────────────────────────
  const mcpManager = new McpClientManager();
  if (cfg.mcpServers.length > 0) {
    display.info(`Connecting to ${cfg.mcpServers.length} MCP server(s)…`);
    try {
      await mcpManager.connect(cfg.mcpServers);
      const tools = await mcpManager.listAllTools();
      display.info(`MCP tools available: ${tools.map((t) => t.name).join(", ") || "(none)"}`);
    } catch (err) {
      display.warn(`MCP connection failed: ${err instanceof Error ? err.message : String(err)}`);
    }
  } else {
    display.info("No MCP servers configured. Running without tool support.");
  }

  display.separator();
  display.info('Type your message and press Enter, or just press Enter to use the microphone.');
  display.info('Type "exit" or press Ctrl+C to quit.');
  display.separator();

  // ── Conversation loop ──────────────────────────────────────
  const conversationHistory: AgentMessage[] = [];
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

  const askPrompt = (): void => {
    display.prompt();
    rl.once("line", async (line) => {
      const trimmed = line.trim();

      if (trimmed.toLowerCase() === "exit") {
        await shutdown();
        return;
      }

      let userText = trimmed;

      if (!userText) {
        // No typed text — use microphone
        display.recording();

        const openaiClient =
          cfg.llmProvider === "openai" ? (llmClient as OpenAILLMClient) : null;

        if (!openaiClient) {
          display.warn(
            "Voice transcription requires the OpenAI provider (Whisper). " +
              "Please type your message instead.",
          );
          askPrompt();
          return;
        }

        let recording;
        try {
          recording = await recordAudio(cfg.maxRecordMs);
          display.transcribing();
          userText = await openaiClient.transcribeAudio(recording.filePath);
        } catch (err) {
          display.error(
            `Recording/transcription failed: ${err instanceof Error ? err.message : String(err)}`,
          );
          await recording?.cleanup();
          askPrompt();
          return;
        }
        await recording.cleanup();

        if (!userText.trim()) {
          display.warn("No speech detected. Please try again.");
          askPrompt();
          return;
        }
      }

      // Run the agentic loop
      try {
        const reply = await runAgentTurn(userText, conversationHistory, llmClient, mcpManager);
        // Add the turn to history
        conversationHistory.push({ role: "user", content: userText });
        conversationHistory.push({ role: "assistant", content: reply });
      } catch (err) {
        display.error(`Agent error: ${err instanceof Error ? err.message : String(err)}`);
      }

      askPrompt();
    });
  };

  const shutdown = async (): Promise<void> => {
    display.info("Shutting down…");
    rl.close();
    await mcpManager.disconnectAll();
    process.exit(0);
  };

  process.on("SIGINT", () => void shutdown());

  askPrompt();
}

main().catch((err: unknown) => {
  console.error("Fatal error:", err);
  process.exit(1);
});
