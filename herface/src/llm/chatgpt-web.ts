import { mkdir, readFile, writeFile } from "fs/promises";
import { resolve } from "path";

import { chromium, type BrowserContext, type Locator, type Page } from "playwright";

import type { Config } from "../config.js";
import type { AgentMessage, ChatResult, LLMClient, ToolDefinition } from "./types.js";

type ChatGptWebConfig = Config["chatgptWeb"];

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

const COMPOSER_SELECTORS = [
  'textarea[data-testid="prompt-textarea"]',
  "#prompt-textarea",
  'textarea[placeholder*="Message"]',
  'div[contenteditable="true"][data-testid*="composer"]',
  'div[contenteditable="true"][aria-label*="Message"]',
];

const SEND_BUTTON_SELECTORS = [
  'button[data-testid="send-button"]',
  'button[aria-label*="Send prompt"]',
  'button[aria-label*="Send message"]',
  'button[aria-label="Send"]',
];

const NEW_CHAT_SELECTORS = [
  'a[href="/"]',
  'button[data-testid="create-new-chat-button"]',
  'button[aria-label*="New chat"]',
  'a[aria-label*="New chat"]',
];

const ASSISTANT_MESSAGE_SELECTORS = [
  '[data-message-author-role="assistant"]',
  'article[data-testid^="conversation-turn-"] [data-message-author-role="assistant"]',
];

const LOGIN_HINT_SELECTORS = [
  'button[data-testid="login-button"]',
  'a[href*="/auth/login"]',
  'button:has-text("Log in")',
];

const STOP_BUTTON_SELECTORS = [
  'button[aria-label*="Stop"]',
  'button[data-testid="stop-button"]',
];

export class ChatGptWebProvider implements LLMClient {
  readonly providerId = "chatgpt-web" as const;
  readonly displayName: string;

  private context: BrowserContext | null = null;
  private page: Page | null = null;
  private readonly sessionStatePath: string;
  private currentSessionId: string | null = null;

  constructor(private readonly config: ChatGptWebConfig) {
    this.displayName = `ChatGPT Web (${config.browserChannel}, project ${config.projectName})`;
    this.sessionStatePath = resolve(config.profileDir, "herface-chatgpt-sessions.json");
  }

  async chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult> {
    const page = await this.getPage();
    await this.prepareFreshChat(page);
    await this.touchSession(page.url());

    const prompt = buildChatGptBridgePrompt(messages, tools, this.config.projectName);
    const beforeText = await this.getLatestAssistantText(page);

    await this.fillComposer(page, prompt);
    await this.submitPrompt(page);

    const rawReply = await this.waitForAssistantReply(page, beforeText);
    await this.touchSession(page.url());
    return parseChatGptBridgeReply(rawReply);
  }

  async shutdown(): Promise<void> {
    if (this.context) {
      await this.context.close();
    }
    this.context = null;
    this.page = null;
  }

  private async getPage(): Promise<Page> {
    if (this.page && !this.page.isClosed()) {
      return this.page;
    }

    await mkdir(this.config.profileDir, { recursive: true });
    await this.cleanupExpiredSessions();

    const launchOptions: Parameters<typeof chromium.launchPersistentContext>[1] = {
      headless: this.config.headless,
      viewport: { width: 1440, height: 1024 },
    };

    if (this.config.browserChannel !== "chromium") {
      launchOptions.channel = this.config.browserChannel;
    }

    this.context = await chromium.launchPersistentContext(this.config.profileDir, launchOptions);
    this.page = this.context.pages()[0] ?? (await this.context.newPage());

    await this.page.goto(this.config.baseUrl, { waitUntil: "domcontentloaded" });
    await this.waitForChatReady(this.page);
    return this.page;
  }

  private async waitForChatReady(page: Page): Promise<void> {
    const startedAt = Date.now();

    while (Date.now() - startedAt < this.config.startupTimeoutMs) {
      const composer = await firstVisible(page, COMPOSER_SELECTORS);
      if (composer) {
        return;
      }

      if (await anyVisible(page, LOGIN_HINT_SELECTORS)) {
        throw new Error(
          "ChatGPT login is required. The browser profile was opened for you; sign in there and try again.",
        );
      }

      await page.waitForTimeout(500);
    }

    throw new Error("Timed out waiting for the ChatGPT composer to become available.");
  }

  private async prepareFreshChat(page: Page): Promise<void> {
    await page.goto(this.config.baseUrl, { waitUntil: "domcontentloaded" });
    await this.waitForChatReady(page);

    const newChatButton = await firstVisible(page, NEW_CHAT_SELECTORS);
    if (newChatButton) {
      await newChatButton.click().catch(() => undefined);
      await page.waitForTimeout(400);
    }
  }

  private async fillComposer(page: Page, prompt: string): Promise<void> {
    const composer = await firstVisible(page, COMPOSER_SELECTORS);
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
    const sendButton = await firstVisible(page, SEND_BUTTON_SELECTORS);
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

    while (Date.now() - startedAt < this.config.responseTimeoutMs) {
      const currentText = await this.getLatestAssistantText(page);
      const generating = await anyVisible(page, STOP_BUTTON_SELECTORS);

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

      await page.waitForTimeout(1000);
    }

    throw new Error("Timed out waiting for a ChatGPT browser response.");
  }

  private async getLatestAssistantText(page: Page): Promise<string> {
    for (const selector of ASSISTANT_MESSAGE_SELECTORS) {
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
}

async function firstVisible(page: Page, selectors: string[]): Promise<Locator | null> {
  for (const selector of selectors) {
    const locator = page.locator(selector).first();
    if (await locator.isVisible().catch(() => false)) {
      return locator;
    }
  }

  return null;
}

async function anyVisible(page: Page, selectors: string[]): Promise<boolean> {
  return (await firstVisible(page, selectors)) !== null;
}

async function tryFill(locator: Locator, value: string): Promise<boolean> {
  try {
    await locator.fill(value);
    return true;
  } catch {
    return false;
  }
}

function buildChatGptBridgePrompt(
  messages: AgentMessage[],
  tools: ToolDefinition[],
  projectName: string,
): string {
  const transcript = messages.map((message) => serializeAgentMessage(message)).join("\n");
  const toolSpec = JSON.stringify(tools, null, 2);

  return [
    `You are the browser-backed ChatGPT adapter for the local project "${projectName}".`,
    "Respond with JSON only. Do not wrap the response in markdown unless it is a fenced ```json block.",
    'Return an object with shape {"text": string | null, "toolCalls": [{"id": string, "name": string, "arguments": string}]}.',
    'If you need a tool, emit one or more toolCalls and keep "arguments" as a JSON-encoded string.',
    'If you do not need a tool, return "toolCalls": [].',
    "Only use tools from the provided schema.",
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
