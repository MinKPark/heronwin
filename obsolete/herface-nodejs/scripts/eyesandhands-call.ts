import path from "node:path";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

type TextContent = { type: string; text?: string };
type ToolCall = { toolName: string; args: Record<string, unknown> };

function extractText(content: TextContent[]): string {
  return content
    .filter((item) => item.type === "text")
    .map((item) => item.text ?? "")
    .join("\n");
}

function parseArguments(raw: string | undefined): Record<string, unknown> {
  if (!raw || raw.trim() === "") {
    return {};
  }

  const parsed = JSON.parse(raw) as unknown;
  if (parsed === null || Array.isArray(parsed) || typeof parsed !== "object") {
    throw new Error("Tool arguments must be a JSON object.");
  }

  return parsed as Record<string, unknown>;
}

function parseCalls(tokens: string[]): ToolCall[] {
  if (tokens.length === 0) {
    throw new Error(
      'Usage: npm run mcp:eyesandhands:call -- <tool_name> [\'{"key":"value"}\'] [<tool_name> [\'{"key":"value"}\']]...',
    );
  }

  const calls: ToolCall[] = [];
  let index = 0;

  while (index < tokens.length) {
    const toolName = tokens[index];
    if (!toolName) {
      throw new Error("Tool name is required.");
    }

    const nextToken = tokens[index + 1];
    const hasJsonArgs =
      nextToken !== undefined &&
      (nextToken.trim().startsWith("{") || nextToken.trim().startsWith("["));

    calls.push({
      toolName,
      args: parseArguments(hasJsonArgs ? nextToken : undefined),
    });

    index += hasJsonArgs ? 2 : 1;
  }

  return calls;
}

async function main(): Promise<void> {
  const calls = parseCalls(process.argv.slice(2));
  const __dirname = path.dirname(fileURLToPath(import.meta.url));
  const serverPath = path.resolve(
    __dirname,
    "../../herbody/eyesandhands/bin/Debug/net10.0-windows/eyesandhands.exe",
  );

  const transport = new StdioClientTransport({ command: serverPath });
  const client = new Client({ name: "eyesandhands-call", version: "1.0.0" });

  try {
    await client.connect(transport);
    for (const call of calls) {
      const result = await client.callTool({
        name: call.toolName,
        arguments: call.args,
      });

      console.log(`=== ${call.toolName} ===`);
      console.log(extractText(result.content as TextContent[]));
    }
  } finally {
    await client.close();
  }
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
