/** A single tool available to the LLM (converted from MCP tool schema). */
export interface ToolDefinition {
  name: string;
  description: string;
  /** JSON Schema object describing the input parameters. */
  parameters: Record<string, unknown>;
}

/** A tool call requested by the LLM. */
export interface ToolCallRequest {
  id: string;
  name: string;
  /** Raw JSON-encoded arguments string from the LLM. */
  arguments: string;
}

/** A message in the agent conversation. */
export type AgentMessage =
  | { role: "user"; content: string }
  | { role: "assistant"; content: string | null; toolCalls?: ToolCallRequest[] }
  | { role: "tool_result"; toolCallId: string; toolName: string; content: string };

/** Result returned by an LLM backend after one inference step. */
export interface ChatResult {
  /** The text response from the LLM, or null when only tool calls are returned. */
  text: string | null;
  /** Any tool calls the LLM wants to invoke. Empty array when none. */
  toolCalls: ToolCallRequest[];
}

export type LlmProviderId = "openai-api" | "chatgpt-web" | "claude-api";

/** Common interface implemented by every chat backend. */
export interface LLMClient {
  readonly providerId: LlmProviderId;
  readonly displayName: string;

  /**
   * Send the conversation so far to the LLM and return its response.
   * @param messages - Conversation history
   * @param tools    - Available tools the LLM may call
   */
  chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult>;

  /** Optional cleanup hook for browser-backed providers. */
  shutdown?(): Promise<void>;
}

/** Speech-to-text is a separate capability from chat completion. */
export interface AudioTranscriber {
  readonly displayName: string;
  transcribeAudio(audioFilePath: string): Promise<string>;
}
