import { config } from "dotenv";

config();

export interface McpServerConfig {
  name: string;
  command: string;
  args?: string[];
  env?: Record<string, string>;
}

export interface Config {
  llmProvider: "openai" | "claude";
  openaiApiKey: string;
  openaiModel: string;
  anthropicApiKey: string;
  anthropicModel: string;
  whisperModel: string;
  maxRecordMs: number;
  mcpServers: McpServerConfig[];
}

export function loadConfig(): Config {
  const provider = process.env.LLM_PROVIDER ?? "openai";
  if (provider !== "openai" && provider !== "claude") {
    throw new Error(`Invalid LLM_PROVIDER "${provider}". Must be "openai" or "claude".`);
  }

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
    openaiModel: process.env.OPENAI_MODEL ?? "gpt-4o",
    anthropicApiKey: process.env.ANTHROPIC_API_KEY ?? "",
    anthropicModel: process.env.ANTHROPIC_MODEL ?? "claude-3-5-sonnet-20241022",
    whisperModel: process.env.WHISPER_MODEL ?? "whisper-1",
    maxRecordMs: parseInt(process.env.MAX_RECORD_MS ?? "30000", 10),
    mcpServers,
  };
}
