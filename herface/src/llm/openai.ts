import OpenAI from "openai";
import { createReadStream } from "fs";
import type { AgentMessage, ChatResult, LLMClient, ToolDefinition } from "./types.js";
import type {
  ChatCompletionMessageParam,
  ChatCompletionTool,
} from "openai/resources/chat/completions.js";

export class OpenAILLMClient implements LLMClient {
  private client: OpenAI;
  private model: string;
  private whisperModel: string;

  constructor(apiKey: string, model: string, whisperModel: string) {
    this.client = new OpenAI({ apiKey });
    this.model = model;
    this.whisperModel = whisperModel;
  }

  async chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult> {
    const openaiMessages = toOpenAIMessages(messages);
    const openaiTools: ChatCompletionTool[] | undefined =
      tools.length > 0 ? toOpenAITools(tools) : undefined;

    const response = await this.client.chat.completions.create({
      model: this.model,
      messages: openaiMessages,
      ...(openaiTools ? { tools: openaiTools, tool_choice: "auto" } : {}),
    });

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
    const transcription = await this.client.audio.transcriptions.create({
      file: createReadStream(audioFilePath),
      model: this.whisperModel,
    });
    return transcription.text;
  }
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
