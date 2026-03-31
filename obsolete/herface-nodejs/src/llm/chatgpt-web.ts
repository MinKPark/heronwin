import { mkdir, readFile, writeFile } from "fs/promises";
import { resolve } from "path";

import type { Browser, BrowserContext, Page } from "playwright";

import type { Config } from "../config.js";
import { bootstrapChatGptLogin, launchChatGptBrowser } from "./chatgpt-auth.js";
import {
  CHATGPT_ASSISTANT_MESSAGE_SELECTORS,
  CHATGPT_COMPOSER_SELECTORS,
  CHATGPT_NEW_CHAT_SELECTORS,
  CHATGPT_SEND_BUTTON_SELECTORS,
  CHATGPT_STOP_BUTTON_SELECTORS,
  ChatGptWebConfig,
  detectChatGptUiState,
  ensureChatGptStorage,
  firstVisible,
  hasChatGptAuthState,
  tryFill,
} from "./chatgpt-shared.js";
import type { AgentMessage, ChatResult, LLMClient, ToolDefinition } from "./types.js";

interface ChatGptSessionRecord {
  id: string;
  projectName: string;
  createdAt: string;
  lastUsedAt: string;
  lastUrl: string | null;
}

interface ChatGptSessionState {
  projectName: string;
  retentionDays: number;
  sessions: ChatGptSessionRecord[];
}

export class ChatGptWebProvider implements LLMClient {
  readonly providerId = "chatgpt-web" as const;
  readonly displayName: string;

  private browser: Browser | null = null;
  private context: BrowserContext | null = null;
  private page: Page | null = null;
  private readonly sessionStatePath: string;
  private readonly diagnosticsDir: string;
  private currentSessionId: string | null = null;

  constructor(
    private readonly config: ChatGptWebConfig,
    private readonly agentDefinition: string,
  ) {
    this.displayName = `ChatGPT Web (${config.browserChannel}, project ${config.projectName})`;
    this.sessionStatePath = resolve(config.profileDir, "herface-chatgpt-sessions.json");
    this.diagnosticsDir = resolve(config.profileDir, "diagnostics");
  }

  async chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult> {
    console.log("[chatgpt-web] Starting browser-backed turn.");
    const page = await this.getPage();
    console.log("[chatgpt-web] Browser page ready.");
    await this.prepareFreshChat(page);
    console.log("[chatgpt-web] Fresh chat prepared.");
    await this.touchSession(page.url());

    const prompt = buildChatGptBridgePrompt(
      messages,
      tools,
      this.config.projectName,
      this.agentDefinition,
    );
    const beforeText = await this.getLatestAssistantText(page);

    console.log("[chatgpt-web] Sending prompt.");
    await this.fillComposer(page, prompt);
    await this.submitPrompt(page);

    console.log("[chatgpt-web] Waiting for assistant reply.");
    const rawReply = await this.waitForAssistantReply(page, beforeText);
    console.log("[chatgpt-web] Assistant reply received.");
    await this.touchSession(page.url());
    return parseChatGptBridgeReply(rawReply);
  }

  async shutdown(): Promise<void> {
    if (this.context) {
      await this.context.close();
    }
    if (this.browser) {
      await this.browser.close();
    }
    this.browser = null;
    this.context = null;
    this.page = null;
  }

  private async getPage(): Promise<Page> {
    if (this.page && !this.page.isClosed()) {
      console.log("[chatgpt-web] Reusing existing browser page.");
      return this.page;
    }

    await ensureChatGptStorage(this.config);
    await this.cleanupExpiredSessions();
    console.log("[chatgpt-web] Opening authenticated browser page.");
    return this.openAuthenticatedPage(true);
  }

  private async openAuthenticatedPage(allowReauth: boolean): Promise<Page> {
    await this.ensureAuthenticatedSession(
      allowReauth ? "No saved ChatGPT login was found." : null,
    );

    console.log(
      `[chatgpt-web] Launching ${this.config.headless ? "playwright chromium" : this.config.browserChannel} (${this.config.headless ? "headless" : "visible"}) with saved auth.`,
    );
    const { browser, context, page } = await launchChatGptBrowser(
      this.config,
      this.config.headless,
      true,
    );
    this.browser = browser;
    this.context = context;
    this.page = page;

    await this.page.goto(this.config.baseUrl, { waitUntil: "domcontentloaded" });
    const state = await detectChatGptUiState(this.page, this.config.startupTimeoutMs);
    console.log(`[chatgpt-web] Initial page state: ${state}`);
    if (state === "ready") {
      return this.page;
    }

    await this.shutdown();

    if (state === "login_required" && allowReauth) {
      await bootstrapChatGptLogin(this.config, "Saved ChatGPT login expired. Please sign in again.");
      return this.openAuthenticatedPage(false);
    }

    if (state === "login_required") {
      throw new Error("ChatGPT login is required. Re-run the login bootstrap to refresh the saved session.");
    }

    await this.writeDiagnostics("initial-page-timeout", this.page);
    throw new Error("Timed out waiting for the ChatGPT composer to become available.");
  }

  private async prepareFreshChat(page: Page): Promise<void> {
    await page.goto(this.config.baseUrl, { waitUntil: "domcontentloaded" });
    const state = await detectChatGptUiState(page, this.config.startupTimeoutMs);
    console.log(`[chatgpt-web] Fresh chat page state: ${state}`);
    if (state === "login_required") {
      await this.shutdown();
      await bootstrapChatGptLogin(this.config, "Saved ChatGPT login expired. Please sign in again.");
      throw new Error("ChatGPT login was refreshed. Please send your message again.");
    }
    if (state !== "ready") {
      await this.writeDiagnostics("fresh-chat-not-ready", page);
      throw new Error("ChatGPT page was not ready when preparing a fresh chat.");
    }

    const newChatButton = await firstVisible(page, CHATGPT_NEW_CHAT_SELECTORS);
    if (newChatButton) {
      await newChatButton.click().catch(() => undefined);
      await page.waitForTimeout(400);
    }
  }

  private async fillComposer(page: Page, prompt: string): Promise<void> {
    const composer = await firstVisible(page, CHATGPT_COMPOSER_SELECTORS);
    if (!composer) {
      throw new Error("Could not find the ChatGPT message composer.");
    }

    await composer.click();
    if (!(await tryFill(composer, prompt))) {
      await page.keyboard.press(process.platform === "darwin" ? "Meta+A" : "Control+A");
      await page.keyboard.insertText(prompt);
    }
  }

  private async submitPrompt(page: Page): Promise<void> {
    const sendButton = await firstVisible(page, CHATGPT_SEND_BUTTON_SELECTORS);
    if (sendButton) {
      await sendButton.click();
      return;
    }

    await page.keyboard.press("Enter");
  }

  private async waitForAssistantReply(page: Page, previousText: string): Promise<string> {
    const startedAt = Date.now();
    let latestText = previousText;
    let stableTicks = 0;
    let lastLogAt = 0;

    while (Date.now() - startedAt < this.config.responseTimeoutMs) {
      const currentText = await this.getLatestAssistantText(page);
      const generating = (await firstVisible(page, CHATGPT_STOP_BUTTON_SELECTORS)) !== null;

      if (currentText && currentText !== previousText) {
        if (currentText === latestText) {
          stableTicks += 1;
        } else {
          latestText = currentText;
          stableTicks = 0;
        }

        if (!generating && stableTicks >= 2) {
          return latestText;
        }
      }

      if (Date.now() - lastLogAt > 5000) {
        console.log(
          `[chatgpt-web] Still waiting... generating=${generating} currentTextLength=${currentText.length}`,
        );
        lastLogAt = Date.now();
      }

      await page.waitForTimeout(1000);
    }

    throw new Error("Timed out waiting for a ChatGPT browser response.");
  }

  private async getLatestAssistantText(page: Page): Promise<string> {
    for (const selector of CHATGPT_ASSISTANT_MESSAGE_SELECTORS) {
      const items = page.locator(selector);
      const count = await items.count();
      if (count === 0) {
        continue;
      }

      const latest = items.nth(count - 1);
      const text = (await latest.innerText().catch(() => latest.textContent())) ?? "";
      if (text.trim()) {
        return text.trim();
      }
    }

    return "";
  }

  private async cleanupExpiredSessions(): Promise<void> {
    const state = await this.readSessionState();
    const cutoff = Date.now() - this.config.sessionRetentionDays * 24 * 60 * 60 * 1000;
    const filtered = state.sessions.filter((session) => Date.parse(session.lastUsedAt) >= cutoff);

    if (filtered.length !== state.sessions.length) {
      await this.writeSessionState({ ...state, sessions: filtered });
    }
  }

  private async touchSession(lastUrl: string | null): Promise<void> {
    const state = await this.readSessionState();
    const now = new Date().toISOString();

    if (!this.currentSessionId) {
      this.currentSessionId = `chatgpt-${Date.now()}`;
      state.sessions.unshift({
        id: this.currentSessionId,
        projectName: this.config.projectName,
        createdAt: now,
        lastUsedAt: now,
        lastUrl,
      });
    } else {
      state.sessions = state.sessions.map((session) =>
        session.id === this.currentSessionId
          ? { ...session, lastUsedAt: now, lastUrl }
          : session,
      );
    }

    await this.writeSessionState(state);
  }

  private async readSessionState(): Promise<ChatGptSessionState> {
    try {
      const raw = await readFile(this.sessionStatePath, "utf8");
      return JSON.parse(raw) as ChatGptSessionState;
    } catch {
      return {
        projectName: this.config.projectName,
        retentionDays: this.config.sessionRetentionDays,
        sessions: [],
      };
    }
  }

  private async writeSessionState(state: ChatGptSessionState): Promise<void> {
    await writeFile(this.sessionStatePath, JSON.stringify(state, null, 2), "utf8");
  }

  private async writeDiagnostics(reason: string, page: Page): Promise<void> {
    try {
      await mkdir(this.diagnosticsDir, { recursive: true });
      const stamp = new Date().toISOString().replace(/[:.]/g, "-");
      const prefix = resolve(this.diagnosticsDir, `${stamp}-${reason}`);

      const html = await page.content().catch(() => "");
      const title = await page.title().catch(() => "");
      const url = page.url();

      await writeFile(
        `${prefix}.json`,
        JSON.stringify({ reason, url, title }, null, 2),
        "utf8",
      );
      await writeFile(`${prefix}.html`, html, "utf8");
      await page.screenshot({ path: `${prefix}.png`, fullPage: true }).catch(() => undefined);

      console.log(`[chatgpt-web] Wrote diagnostics to ${prefix}.*`);
    } catch (error) {
      console.log(
        `[chatgpt-web] Failed to write diagnostics: ${error instanceof Error ? error.message : String(error)}`,
      );
    }
  }

  private async ensureAuthenticatedSession(reason: string | null): Promise<void> {
    if (await hasChatGptAuthState(this.config)) {
      console.log("[chatgpt-web] Found saved ChatGPT auth state.");
      return;
    }

    if (!reason) {
      throw new Error("ChatGPT auth state is missing.");
    }

    await bootstrapChatGptLogin(this.config, reason);
  }
}

function buildChatGptBridgePrompt(
  messages: AgentMessage[],
  tools: ToolDefinition[],
  projectName: string,
  agentDefinition: string,
): string {
  const transcript = messages.map((message) => serializeAgentMessage(message)).join("\n");
  const toolSpec = JSON.stringify(tools, null, 2);
  const trimmedAgentDefinition = agentDefinition.trim();

  return [
    `You are the browser-backed ChatGPT adapter for the local project "${projectName}".`,
    "Respond with JSON only. Do not wrap the response in markdown unless it is a fenced ```json block.",
    'Return an object with shape {"text": string | null, "toolCalls": [{"id": string, "name": string, "arguments": string}]}.',
    'If you need a tool, emit one or more toolCalls and keep "arguments" as a JSON-encoded string.',
    'If you do not need a tool, return "toolCalls": [].',
    "Only use tools from the provided schema.",
    ...(trimmedAgentDefinition
      ? ["", "Agent definition:", trimmedAgentDefinition]
      : []),
    "",
    "Available tools:",
    toolSpec,
    "",
    "Conversation transcript:",
    transcript,
  ].join("\n");
}

function serializeAgentMessage(message: AgentMessage): string {
  switch (message.role) {
    case "user":
      return `USER: ${message.content}`;
    case "assistant":
      return `ASSISTANT: ${JSON.stringify({
        content: message.content,
        toolCalls: message.toolCalls ?? [],
      })}`;
    case "tool_result":
      return `TOOL_RESULT: ${JSON.stringify({
        toolCallId: message.toolCallId,
        toolName: message.toolName,
        content: message.content,
      })}`;
  }
}

function parseChatGptBridgeReply(rawReply: string): ChatResult {
  const jsonText = extractJsonPayload(rawReply);
  if (!jsonText) {
    return { text: rawReply.trim() || null, toolCalls: [] };
  }

  try {
    const parsed = JSON.parse(jsonText) as {
      text?: unknown;
      toolCalls?: Array<{ id?: unknown; name?: unknown; arguments?: unknown }>;
    };

    const toolCalls =
      parsed.toolCalls?.flatMap((toolCall, index) => {
        if (typeof toolCall?.name !== "string" || toolCall.name.length === 0) {
          return [];
        }

        const argumentsText =
          typeof toolCall.arguments === "string"
            ? toolCall.arguments
            : JSON.stringify(toolCall.arguments ?? {});

        return [
          {
            id:
              typeof toolCall.id === "string" && toolCall.id.length > 0
                ? toolCall.id
                : `chatgpt-web-call-${index + 1}`,
            name: toolCall.name,
            arguments: argumentsText,
          },
        ];
      }) ?? [];

    return {
      text: typeof parsed.text === "string" ? parsed.text : null,
      toolCalls,
    };
  } catch {
    return { text: rawReply.trim() || null, toolCalls: [] };
  }
}

function extractJsonPayload(text: string): string | null {
  const fenced = text.match(/```json\s*([\s\S]*?)\s*```/i);
  if (fenced?.[1]) {
    return fenced[1].trim();
  }

  const start = text.indexOf("{");
  const end = text.lastIndexOf("}");
  if (start >= 0 && end > start) {
    return text.slice(start, end + 1).trim();
  }

  return null;
}
