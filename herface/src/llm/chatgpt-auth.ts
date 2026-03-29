import { chromium, type Browser, type BrowserContext, type Page } from "playwright";

import {
  CHATGPT_COMPOSER_SELECTORS,
  ChatGptWebConfig,
  detectChatGptUiState,
  ensureChatGptStorage,
  getChatGptAuthStatePath,
} from "./chatgpt-shared.js";

export async function bootstrapChatGptLogin(
  config: ChatGptWebConfig,
  reason: string,
): Promise<string> {
  await ensureChatGptStorage(config);

  const { browser, context, page } = await launchChatGptBrowser(config, false);
  const authStatePath = getChatGptAuthStatePath(config);

  try {
    console.log(`ChatGPT login required: ${reason}`);
    console.log(`Opening ${config.browserChannel} for one-time authentication...`);
    console.log("Complete login in the opened browser window. The session will be saved for later headless runs.");

    await page.goto(config.baseUrl, { waitUntil: "domcontentloaded" });
    const ready = await waitForChatGptReadyForLogin(page, config.loginTimeoutMs);
    if (!ready) {
      throw new Error("Timed out waiting for ChatGPT login to complete.");
    }

    await context.storageState({ path: authStatePath });
    console.log(`Saved ChatGPT auth state to ${authStatePath}`);
    return authStatePath;
  } finally {
    await context.close();
    await browser.close();
  }
}

export async function launchChatGptBrowser(
  config: ChatGptWebConfig,
  headless: boolean,
  useSavedAuth = false,
): Promise<{ browser: Browser; context: BrowserContext; page: Page }> {
  const browser = await chromium.launch({
    headless,
    ...(config.browserChannel !== "chromium" ? { channel: config.browserChannel } : {}),
  });

  const context = await browser.newContext({
    viewport: { width: 1440, height: 1024 },
    ...(useSavedAuth ? { storageState: getChatGptAuthStatePath(config) } : {}),
  });

  const page = await context.newPage();
  return { browser, context, page };
}

async function waitForChatGptReadyForLogin(page: Page, timeoutMs: number): Promise<boolean> {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    const state = await detectChatGptUiState(page, 1000);
    if (state === "ready") {
      return true;
    }

    if (state === "timeout") {
      await page.reload({ waitUntil: "domcontentloaded" }).catch(() => undefined);
    }

    await page.waitForTimeout(500);
  }

  const composer = await page.locator(CHATGPT_COMPOSER_SELECTORS[0]).count().catch(() => 0);
  return composer > 0;
}
