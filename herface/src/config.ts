import { resolve } from "path";
import { config } from "dotenv";

import type { LlmProviderId } from "./llm/types.js";

config();

export interface McpServerConfig {
  name: string;
  command: string;
  args?: string[];
  env?: Record<string, string>;
}

export interface Config {
  llmProvider: LlmProviderId;
  openaiApiKey: string;
  openaiModel: string;
  anthropicApiKey: string;
  anthropicModel: string;
  whisperModel: string;
  maxRecordMs: number;
  mcpServers: McpServerConfig[];
  chatgptWeb: {
    baseUrl: string;
    browserChannel: "chrome" | "msedge" | "chromium";
    profileDir: string;
    headless: boolean;
    projectName: string;
    sessionRetentionDays: number;
    loginTimeoutMs: number;
    startupTimeoutMs: number;
    responseTimeoutMs: number;
  };
}

export function loadConfig(): Config {
  const provider = normalizeProvider(process.env.LLM_PROVIDER ?? "openai-api");

  let mcpServers: McpServerConfig[] = [];
  const mcpServersRaw = process.env.MCP_SERVERS;
  if (mcpServersRaw && mcpServersRaw !== "[]") {
    try {
      mcpServers = JSON.parse(mcpServersRaw) as McpServerConfig[];
    } catch {
      console.warn('Warning: MCP_SERVERS is not valid JSON — ignoring.');
    }
  }

  return {
    llmProvider: provider,
    openaiApiKey: process.env.OPENAI_API_KEY ?? "",
    openaiModel: process.env.OPENAI_MODEL ?? "gpt-5.2-chat-latest",
    anthropicApiKey: process.env.ANTHROPIC_API_KEY ?? "",
    anthropicModel: process.env.ANTHROPIC_MODEL ?? "claude-3-5-sonnet-20241022",
    whisperModel: process.env.WHISPER_MODEL ?? "whisper-1",
    maxRecordMs: parseInt(process.env.MAX_RECORD_MS ?? "30000", 10),
    mcpServers,
    chatgptWeb: {
      baseUrl: process.env.CHATGPT_BASE_URL ?? "https://chatgpt.com/",
      browserChannel: parseBrowserChannel(process.env.CHATGPT_BROWSER_CHANNEL),
      profileDir: resolve(process.cwd(), process.env.CHATGPT_PROFILE_DIR ?? ".chatgpt-profile"),
      headless: parseBoolean(process.env.CHATGPT_HEADLESS, true),
      projectName: process.env.CHATGPT_PROJECT_NAME ?? "her",
      sessionRetentionDays: parseInt(process.env.CHATGPT_SESSION_RETENTION_DAYS ?? "14", 10),
      loginTimeoutMs: parseInt(process.env.CHATGPT_LOGIN_TIMEOUT_MS ?? "900000", 10),
      startupTimeoutMs: parseInt(process.env.CHATGPT_STARTUP_TIMEOUT_MS ?? "120000", 10),
      responseTimeoutMs: parseInt(process.env.CHATGPT_RESPONSE_TIMEOUT_MS ?? "120000", 10),
    },
  };
}

function normalizeProvider(value: string): LlmProviderId {
  switch (value.trim().toLowerCase()) {
    case "openai":
    case "openai-api":
      return "openai-api";
    case "claude":
    case "claude-api":
      return "claude-api";
    case "gpt":
    case "chatgpt":
    case "chatgpt-web":
      return "chatgpt-web";
    default:
      throw new Error(
        `Invalid LLM_PROVIDER "${value}". Must be "openai-api", "chatgpt-web", or "claude-api".`,
      );
  }
}

function parseBrowserChannel(value: string | undefined): "chrome" | "msedge" | "chromium" {
  switch ((value ?? "msedge").trim().toLowerCase()) {
    case "chrome":
      return "chrome";
    case "msedge":
    case "edge":
      return "msedge";
    case "chromium":
      return "chromium";
    default:
      throw new Error(
        `Invalid CHATGPT_BROWSER_CHANNEL "${value}". Must be "chrome", "msedge", or "chromium".`,
      );
  }
}

function parseBoolean(value: string | undefined, fallback: boolean): boolean {
  if (value == null || value.trim() === "") {
    return fallback;
  }

  switch (value.trim().toLowerCase()) {
    case "1":
    case "true":
    case "yes":
    case "on":
      return true;
    case "0":
    case "false":
    case "no":
    case "off":
      return false;
    default:
      throw new Error(`Invalid boolean value "${value}".`);
  }
}
