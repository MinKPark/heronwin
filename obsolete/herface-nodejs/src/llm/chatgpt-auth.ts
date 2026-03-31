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
    console.log("Complete login in the opened browser window.");
    console.log("After ChatGPT is ready, keep the window open as long as you want, then close it yourself.");

    await page.goto(config.baseUrl, { waitUntil: "domcontentloaded" });
    const ready = await waitForChatGptReadyForLogin(page, config.loginTimeoutMs);
    if (!ready) {
      throw new Error("Timed out waiting for ChatGPT login to complete.");
    }

    await context.storageState({ path: authStatePath });
    console.log(`Saved ChatGPT auth state to ${authStatePath}`);
    console.log("Close the browser window when you are done. herface will continue after it closes.");

    await keepAuthStateFreshUntilBrowserCloses(browser, context, authStatePath);
    return authStatePath;
  } finally {
    if (browser.isConnected()) {
      await context.close().catch(() => undefined);
      await browser.close().catch(() => undefined);
    }
  }
}

export async function launchChatGptBrowser(
  config: ChatGptWebConfig,
  headless: boolean,
  useSavedAuth = false,
): Promise<{ browser: Browser; context: BrowserContext; page: Page }> {
  const useChannel = !headless && config.browserChannel !== "chromium";
  const browser = await chromium.launch({
    headless,
    ...(useChannel ? { channel: config.browserChannel } : {}),
    timeout: config.startupTimeoutMs,
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

async function keepAuthStateFreshUntilBrowserCloses(
  browser: Browser,
  context: BrowserContext,
  authStatePath: string,
): Promise<void> {
  while (browser.isConnected()) {
    try {
      await context.storageState({ path: authStatePath });
    } catch {
      break;
    }

    await new Promise<void>((resolve) => setTimeout(resolve, 3000));
  }
}
