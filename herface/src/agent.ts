import type { LLMClient, AgentMessage, ToolDefinition } from "./llm/types.js";
import type { McpClientManager } from "./mcp/client.js";
import { display } from "./ui/display.js";

/**
 * Run a full agentic loop for a single user turn.
 *
 * The loop sends the user message to the LLM, executes any tool calls the LLM
 * requests via the MCP client, feeds the results back, and repeats until the
 * LLM produces a final text response.
 *
 * @returns The final assistant text response.
 */
export async function runAgentTurn(
  userText: string,
  history: AgentMessage[],
  llmClient: LLMClient,
  mcpManager: McpClientManager,
): Promise<string> {
  const tools: ToolDefinition[] = await mcpManager.listAllTools();

  // Add the new user message
  const messages: AgentMessage[] = [...history, { role: "user", content: userText }];
  display.userMessage(userText);

  // Agentic loop — keep calling the LLM until it stops requesting tools
  for (;;) {
    const result = await llmClient.chat(messages, tools);

    if (!result.toolCalls || result.toolCalls.length === 0) {
      // Final response
      const responseText = result.text ?? "(no response)";
      display.assistantMessage(responseText);
      messages.push({ role: "assistant", content: responseText });
      return responseText;
    }

    // LLM wants to call tools — record assistant message with tool call requests
    messages.push({
      role: "assistant",
      content: result.text,
      toolCalls: result.toolCalls,
    });

    // Execute each tool call via MCP
    for (const tc of result.toolCalls) {
      let parsedArgs: unknown = {};
      try {
        parsedArgs = JSON.parse(tc.arguments);
      } catch {
        parsedArgs = {};
      }

      display.toolCall(tc.name, tc.arguments);

      let toolOutput: string;
      try {
        toolOutput = await mcpManager.callTool(tc.name, parsedArgs);
      } catch (err) {
        toolOutput = `Error: ${err instanceof Error ? err.message : String(err)}`;
      }

      display.toolResult(tc.name, toolOutput);

      messages.push({
        role: "tool_result",
        toolCallId: tc.id,
        toolName: tc.name,
        content: toolOutput,
      });
    }
  }
}
