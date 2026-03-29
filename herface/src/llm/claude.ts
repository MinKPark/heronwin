import Anthropic from "@anthropic-ai/sdk";
import type { AgentMessage, ChatResult, LLMClient, ToolDefinition } from "./types.js";
import type {
  MessageParam,
  Tool,
  TextBlockParam,
  ToolResultBlockParam,
  ToolUseBlockParam,
  ToolUseBlock,
} from "@anthropic-ai/sdk/resources/messages.js";

export class ClaudeLLMClient implements LLMClient {
  private client: Anthropic;
  private model: string;

  constructor(apiKey: string, model: string) {
    this.client = new Anthropic({ apiKey });
    this.model = model;
  }

  async chat(messages: AgentMessage[], tools: ToolDefinition[]): Promise<ChatResult> {
    const anthropicMessages = toAnthropicMessages(messages);
    const anthropicTools: Tool[] | undefined =
      tools.length > 0 ? toAnthropicTools(tools) : undefined;

    const response = await this.client.messages.create({
      model: this.model,
      max_tokens: 4096,
      messages: anthropicMessages,
      ...(anthropicTools ? { tools: anthropicTools } : {}),
    });

    let text: string | null = null;
    const toolCalls: ChatResult["toolCalls"] = [];

    for (const block of response.content) {
      if (block.type === "text") {
        text = (text ?? "") + block.text;
      } else if (block.type === "tool_use") {
        const toolUse = block as ToolUseBlock;
        toolCalls.push({
          id: toolUse.id,
          name: toolUse.name,
          arguments: JSON.stringify(toolUse.input),
        });
      }
    }

    return { text, toolCalls };
  }
}

function toAnthropicMessages(messages: AgentMessage[]): MessageParam[] {
  const result: MessageParam[] = [];

  let i = 0;
  while (i < messages.length) {
    const msg = messages[i];

    if (msg.role === "user") {
      result.push({ role: "user", content: msg.content });
      i++;
    } else if (msg.role === "assistant") {
      if (msg.toolCalls && msg.toolCalls.length > 0) {
        // Assistant message with tool calls — build content blocks using param types
        const contentBlocks: Array<TextBlockParam | ToolUseBlockParam> = [];
        if (msg.content) {
          contentBlocks.push({ type: "text", text: msg.content });
        }
        for (const tc of msg.toolCalls) {
          let parsedInput: Record<string, unknown> = {};
          try {
            parsedInput = JSON.parse(tc.arguments) as Record<string, unknown>;
          } catch {
            // keep empty object if parse fails
          }
          contentBlocks.push({
            type: "tool_use",
            id: tc.id,
            name: tc.name,
            input: parsedInput,
          });
        }
        result.push({ role: "assistant", content: contentBlocks });

        // Collect subsequent tool_result messages into a single user turn
        i++;
        const toolResults: ToolResultBlockParam[] = [];
        while (i < messages.length && messages[i].role === "tool_result") {
          const tr = messages[i] as Extract<AgentMessage, { role: "tool_result" }>;
          toolResults.push({
            type: "tool_result",
            tool_use_id: tr.toolCallId,
            content: tr.content,
          });
          i++;
        }
        if (toolResults.length > 0) {
          result.push({ role: "user", content: toolResults });
        }
      } else {
        result.push({ role: "assistant", content: msg.content ?? "" });
        i++;
      }
    } else {
      // skip orphaned tool_result (shouldn't happen in normal flow)
      i++;
    }
  }

  return result;
}

function toAnthropicTools(tools: ToolDefinition[]): Tool[] {
  return tools.map((t) => ({
    name: t.name,
    description: t.description,
    input_schema: t.parameters as Tool["input_schema"],
  }));
}
