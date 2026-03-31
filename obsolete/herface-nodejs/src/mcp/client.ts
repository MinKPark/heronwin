import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import type { McpServerConfig } from "../config.js";
import type { ToolDefinition } from "../llm/types.js";

export class McpClientManager {
  private clients = new Map<string, Client>();

  /** Connect to all configured MCP servers. */
  async connect(servers: McpServerConfig[]): Promise<void> {
    for (const cfg of servers) {
      const transport = new StdioClientTransport({
        command: cfg.command,
        args: cfg.args,
        env: cfg.env ? { ...process.env, ...cfg.env } as Record<string, string> : undefined,
      });

      const client = new Client({ name: "herface", version: "1.0.0" });
      await client.connect(transport);
      this.clients.set(cfg.name, client);
    }
  }

  /** Collect all tools from every connected MCP server. */
  async listAllTools(): Promise<Array<ToolDefinition & { serverName: string }>> {
    const tools: Array<ToolDefinition & { serverName: string }> = [];

    for (const [serverName, client] of this.clients) {
      const result = await client.listTools();
      for (const tool of result.tools) {
        tools.push({
          name: tool.name,
          description: tool.description ?? "",
          parameters: tool.inputSchema as Record<string, unknown>,
          serverName,
        });
      }
    }

    return tools;
  }

  /** Execute a tool by name, searching all connected servers. */
  async callTool(toolName: string, args: unknown): Promise<string> {
    for (const client of this.clients.values()) {
      const toolList = await client.listTools();
      const found = toolList.tools.some((t) => t.name === toolName);
      if (!found) continue;

      const result = await client.callTool({
        name: toolName,
        arguments: args as Record<string, unknown>,
      });

      const content = result.content as Array<{ type: string; text?: string }>;
      return content
        .filter((c) => c.type === "text")
        .map((c) => c.text ?? "")
        .join("\n");
    }

    throw new Error(`Tool "${toolName}" not found on any connected MCP server.`);
  }

  /** Disconnect from all MCP servers. */
  async disconnectAll(): Promise<void> {
    for (const client of this.clients.values()) {
      await client.close();
    }
    this.clients.clear();
  }
}
