import { loadConfig } from "../src/config.js";
import { bootstrapChatGptLogin } from "../src/llm/chatgpt-auth.js";

async function main(): Promise<void> {
  const config = loadConfig();
  await bootstrapChatGptLogin(
    config.chatgptWeb,
    "Starting one-time ChatGPT login bootstrap for herface.",
  );
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
