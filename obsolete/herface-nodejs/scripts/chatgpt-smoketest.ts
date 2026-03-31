import { loadConfig } from "../src/config.js";
import { createLlmProvider } from "../src/llm/factory.js";
import type { AgentMessage } from "../src/llm/types.js";

async function main(): Promise<void> {
  const config = loadConfig();
  const provider = createLlmProvider(config);

  const messages: AgentMessage[] = [{ role: "user", content: "hello" }];

  try {
    const result = await provider.chat(messages, []);
    console.log(JSON.stringify(result, null, 2));
  } finally {
    await provider.shutdown?.();
  }
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.stack ?? error.message : String(error));
  process.exit(1);
});
