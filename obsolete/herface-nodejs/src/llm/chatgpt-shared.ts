import { access, mkdir } from "fs/promises";
import { constants as fsConstants } from "fs";
import { resolve } from "path";

import type { Locator, Page } from "playwright";

import type { Config } from "../config.js";

export type ChatGptWebConfig = Config["chatgptWeb"];
export type ChatGptUiState = "ready" | "login_required" | "timeout";

export const CHATGPT_COMPOSER_SELECTORS = [
  'textarea[data-testid="prompt-textarea"]',
  "#prompt-textarea",
  'textarea[placeholder*="Message"]',
  'div[contenteditable="true"][data-testid*="composer"]',
  'div[contenteditable="true"][aria-label*="Message"]',
];

export const CHATGPT_SEND_BUTTON_SELECTORS = [
  'button[data-testid="send-button"]',
  'button[aria-label*="Send prompt"]',
  'button[aria-label*="Send message"]',
  'button[aria-label="Send"]',
];

export const CHATGPT_NEW_CHAT_SELECTORS = [
  'a[href="/"]',
  'button[data-testid="create-new-chat-button"]',
  'button[aria-label*="New chat"]',
  'a[aria-label*="New chat"]',
];

export const CHATGPT_ASSISTANT_MESSAGE_SELECTORS = [
  '[data-message-author-role="assistant"]',
  'article[data-testid^="conversation-turn-"] [data-message-author-role="assistant"]',
];

export const CHATGPT_LOGIN_HINT_SELECTORS = [
  'button[data-testid="login-button"]',
  'a[href*="/auth/login"]',
  'button:has-text("Log in")',
];

export const CHATGPT_STOP_BUTTON_SELECTORS = [
  'button[aria-label*="Stop"]',
  'button[data-testid="stop-button"]',
];

export function getChatGptAuthStatePath(config: ChatGptWebConfig): string {
  return resolve(config.profileDir, "chatgpt-auth-state.json");
}

export async function ensureChatGptStorage(config: ChatGptWebConfig): Promise<void> {
  await mkdir(config.profileDir, { recursive: true });
}

export async function hasChatGptAuthState(config: ChatGptWebConfig): Promise<boolean> {
  try {
    await access(getChatGptAuthStatePath(config), fsConstants.F_OK);
    return true;
  } catch {
    return false;
  }
}

export async function detectChatGptUiState(
  page: Page,
  timeoutMs: number,
): Promise<ChatGptUiState> {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    if (await firstVisible(page, CHATGPT_COMPOSER_SELECTORS)) {
      return "ready";
    }

    if (await anyVisible(page, CHATGPT_LOGIN_HINT_SELECTORS)) {
      return "login_required";
    }

    await page.waitForTimeout(500);
  }

  return "timeout";
}

export async function firstVisible(page: Page, selectors: string[]): Promise<Locator | null> {
  for (const selector of selectors) {
    const locator = page.locator(selector).first();
    if (await locator.isVisible().catch(() => false)) {
      return locator;
    }
  }

  return null;
}

export async function anyVisible(page: Page, selectors: string[]): Promise<boolean> {
  return (await firstVisible(page, selectors)) !== null;
}

export async function tryFill(locator: Locator, value: string): Promise<boolean> {
  try {
    await locator.fill(value);
    return true;
  } catch {
    return false;
  }
}
