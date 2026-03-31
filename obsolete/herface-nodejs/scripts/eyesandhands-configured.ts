import { loadConfig } from "../src/config.js";
import { McpClientManager } from "../src/mcp/client.js";

async function main(): Promise<void> {
  const config = loadConfig();
  if (config.mcpServers.length === 0) {
    throw new Error("No MCP servers are configured in herface/.env.");
  }

  const manager = new McpClientManager();

  try {
    await manager.connect(config.mcpServers);
    const tools = await manager.listAllTools();
    const available = tools.map((tool) => `${tool.serverName}:${tool.name}`);
    console.log(`Connected tools: ${available.join(", ")}`);

    const result = await manager.callTool("list_windows", {});
    console.log(result);
  } finally {
    await manager.disconnectAll();
  }
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
