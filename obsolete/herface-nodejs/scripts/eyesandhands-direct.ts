import path from "node:path";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

function extractText(content: Array<{ type: string; text?: string }>): string {
  return content
    .filter((item) => item.type === "text")
    .map((item) => item.text ?? "")
    .join("\n");
}

async function main(): Promise<void> {
  const __dirname = path.dirname(fileURLToPath(import.meta.url));
  const serverPath = path.resolve(
    __dirname,
    "../../herbody/eyesandhands/bin/Debug/net10.0-windows/eyesandhands.exe",
  );

  const transport = new StdioClientTransport({ command: serverPath });
  const client = new Client({ name: "eyesandhands-direct-test", version: "1.0.0" });

  try {
    await client.connect(transport);

    const toolList = await client.listTools();
    console.log(`Tools: ${toolList.tools.map((tool) => tool.name).join(", ")}`);

    const result = await client.callTool({
      name: "list_windows",
      arguments: {},
    });

    console.log(extractText(result.content as Array<{ type: string; text?: string }>));
  } finally {
    await client.close();
  }
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
