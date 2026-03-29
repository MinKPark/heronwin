import type { Config } from "../config.js";
import { ChatGptWebProvider } from "./chatgpt-web.js";
import { ClaudeLLMClient } from "./claude.js";
import { OpenAIApiProvider, OpenAIWhisperTranscriber } from "./openai.js";
import type { AudioTranscriber, LLMClient } from "./types.js";

export function createLlmProvider(cfg: Config): LLMClient {
  switch (cfg.llmProvider) {
    case "openai-api":
      if (!cfg.openaiApiKey) {
        throw new Error("OPENAI_API_KEY is not set. OpenAI API mode requires an API key.");
      }
      return new OpenAIApiProvider(cfg.openaiApiKey, cfg.openaiModel);

    case "chatgpt-web":
      return new ChatGptWebProvider(cfg.chatgptWeb);

    case "claude-api":
      if (!cfg.anthropicApiKey) {
        throw new Error("ANTHROPIC_API_KEY is not set. Claude API mode requires an API key.");
      }
      return new ClaudeLLMClient(cfg.anthropicApiKey, cfg.anthropicModel);
  }
}

export function createAudioTranscriber(cfg: Config): AudioTranscriber | null {
  if (!cfg.openaiApiKey) {
    return null;
  }

  return new OpenAIWhisperTranscriber(cfg.openaiApiKey, cfg.whisperModel);
}
