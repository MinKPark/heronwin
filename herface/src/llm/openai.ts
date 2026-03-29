import OpenAI from "openai";
import { readFile } from "fs/promises";
import type { AgentMessage, ChatResult, LLMClient, ToolDefinition } from "./types.js";
import type {
  ChatCompletionMessageParam,
  ChatCompletionTool,
} from "openai/resources/chat/completions.js";

export class OpenAILLMClient implements LLMClient {
  private client: OpenAI;
  private apiKey: string;
  private model: string;
  private whisperModel: string;

  constructor(apiKey: string, model: string, whisperModel: string) {
    this.client = new OpenAI({ apiKey });
    this.apiKey = apiKey;
    this.model = model;
    this.whisperModel = whisperModel;
  }

  async chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult> {
    const openaiMessages = toOpenAIMessages(messages);
    const openaiTools: ChatCompletionTool[] | undefined =
      tools.length > 0 ? toOpenAITools(tools) : undefined;

    let response;
    try {
      response = await this.client.chat.completions.create({
        model: this.model,
        messages: openaiMessages,
        ...(openaiTools ? { tools: openaiTools, tool_choice: "auto" } : {}),
      });
    } catch (error) {
      throw toOpenAIError("Chat request failed", error);
    }

    const message = response.choices[0]?.message;
    if (!message) {
      return { text: null, toolCalls: [] };
    }

    const toolCalls =
      message.tool_calls?.map((tc) => ({
        id: tc.id,
        name: tc.function.name,
        arguments: tc.function.arguments,
      })) ?? [];

    return { text: message.content, toolCalls };
  }

  /** Transcribe an audio file using OpenAI Whisper. */
  async transcribeAudio(audioFilePath: string): Promise<string> {
    const fileBuffer = await readFile(audioFilePath);
    const form = new FormData();
    form.append("model", this.whisperModel);
    form.append("file", new Blob([fileBuffer], { type: "audio/wav" }), "recording.wav");

    let response: Response;
    try {
      response = await fetch("https://api.openai.com/v1/audio/transcriptions", {
        method: "POST",
        headers: {
          Authorization: `Bearer ${this.apiKey}`,
        },
        body: form,
      });
    } catch (error) {
      throw toOpenAIError("Transcription request failed", error);
    }

    const responseText = await response.text();
    const payload = parseJsonObject(responseText);

    if (!response.ok) {
      throw new Error(
        `Transcription request failed (${response.status}): ${getOpenAIMessage(payload, responseText)}`,
      );
    }

    return typeof payload?.text === "string" ? payload.text : "";
  }
}

function toOpenAIError(prefix: string, error: unknown): Error {
  if (error instanceof OpenAI.APIError) {
    const details =
      error.code ?? error.status ? ` (${[error.status, error.code].filter(Boolean).join(", ")})` : "";
    return new Error(`${prefix}${details}: ${error.message}`);
  }

  if (error instanceof Error) {
    const cause = error.cause as { code?: string; message?: string } | undefined;
    if (cause?.code || cause?.message) {
      return new Error(
        `${prefix}: ${error.message}${cause.code ? ` [${cause.code}]` : ""}${cause.message ? ` - ${cause.message}` : ""}`,
      );
    }
    return new Error(`${prefix}: ${error.message}`);
  }

  return new Error(`${prefix}: ${String(error)}`);
}

function parseJsonObject(value: string): Record<string, unknown> | null {
  try {
    return JSON.parse(value) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function getOpenAIMessage(payload: Record<string, unknown> | null, fallback: string): string {
  const errorObject = payload?.error;
  if (errorObject && typeof errorObject === "object") {
    const message = (errorObject as { message?: unknown }).message;
    if (typeof message === "string" && message.length > 0) {
      return message;
    }
  }

  return fallback || "OpenAI request failed.";
}

function toOpenAIMessages(messages: AgentMessage[]): ChatCompletionMessageParam[] {
  const result: ChatCompletionMessageParam[] = [];

  for (const msg of messages) {
    if (msg.role === "user") {
      result.push({ role: "user", content: msg.content });
    } else if (msg.role === "assistant") {
      if (msg.toolCalls && msg.toolCalls.length > 0) {
        result.push({
          role: "assistant",
          content: msg.content ?? null,
          tool_calls: msg.toolCalls.map((tc) => ({
            id: tc.id,
            type: "function" as const,
            function: { name: tc.name, arguments: tc.arguments },
          })),
        });
      } else {
        result.push({ role: "assistant", content: msg.content ?? "" });
      }
    } else if (msg.role === "tool_result") {
      result.push({
        role: "tool",
        tool_call_id: msg.toolCallId,
        content: msg.content,
      });
    }
  }

  return result;
}

function toOpenAITools(tools: ToolDefinition[]): ChatCompletionTool[] {
  return tools.map((t) => ({
    type: "function" as const,
    function: {
      name: t.name,
      description: t.description,
      parameters: t.parameters,
    },
  }));
}
