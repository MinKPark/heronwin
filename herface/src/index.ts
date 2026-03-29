import readline from "readline";
import { loadConfig } from "./config.js";
import { createAudioTranscriber, createLlmProvider } from "./llm/factory.js";
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
  try {
    llmClient = createLlmProvider(cfg);
  } catch (err) {
    display.error(err instanceof Error ? err.message : String(err));
    process.exit(1);
  }

  const audioTranscriber = createAudioTranscriber(cfg);
  const allowVoiceInput = cfg.llmProvider !== "chatgpt-web";
  const promptText = allowVoiceInput
    ? "\n🎤  Press Enter to speak (or type your message): "
    : "\n💬  Type your message and press Enter: ";
  display.info(`LLM: ${llmClient.displayName}`);
  if (cfg.llmProvider === "chatgpt-web") {
    display.info(
      `ChatGPT browser mode will use ${cfg.chatgptWeb.browserChannel} with profile ${cfg.chatgptWeb.profileDir} (${cfg.chatgptWeb.headless ? "headless" : "visible"})`,
    );
    display.info("Voice input is disabled in ChatGPT Web mode; typed input goes to the browser-backed provider.");
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
  display.info(
    allowVoiceInput
      ? 'Type your message and press Enter, or just press Enter to use the microphone.'
      : "Type your message and press Enter.",
  );
  display.info('Type "exit" or press Ctrl+C to quit.');
  display.separator();

  // ── Conversation loop ──────────────────────────────────────
  const conversationHistory: AgentMessage[] = [];
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });

  const askPrompt = (): void => {
    display.prompt(promptText);
    rl.once("line", async (line) => {
      const trimmed = line.trim();

      if (trimmed.toLowerCase() === "exit") {
        await shutdown();
        return;
      }

      let userText = trimmed;

      if (!userText) {
        if (!allowVoiceInput) {
          display.warn("ChatGPT Web mode requires typed input. Enter a message to continue.");
          askPrompt();
          return;
        }

        // No typed text — use microphone
        display.recording();

        if (!audioTranscriber) {
          display.warn(
            "Voice transcription requires OPENAI_API_KEY for Whisper. Please type your message instead.",
          );
          askPrompt();
          return;
        }

        let recording;
        try {
          recording = await recordAudio(cfg.maxRecordMs);
          display.transcribing();
          userText = await audioTranscriber.transcribeAudio(recording.filePath);
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
    await llmClient.shutdown?.();
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
