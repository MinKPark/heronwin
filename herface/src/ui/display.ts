import chalk from "chalk";

const LABEL_WIDTH = 12;

function label(text: string, color: (s: string) => string): string {
  return color(`[${text.padEnd(LABEL_WIDTH)}]`);
}

export const display = {
  /** Print the human/user's transcribed speech. */
  userMessage(text: string): void {
    console.log(`\n${label("You", chalk.cyan)} ${chalk.white(text)}`);
  },

  /** Print the LLM's text response. */
  assistantMessage(text: string): void {
    console.log(`\n${label("Assistant", chalk.green)} ${chalk.white(text)}`);
  },

  /** Print a tool invocation. */
  toolCall(toolName: string, args: string): void {
    console.log(
      `\n${label("Tool call", chalk.yellow)} ${chalk.bold(toolName)}` +
        (args !== "{}" ? `\n${" ".repeat(LABEL_WIDTH + 3)}${chalk.dim(args)}` : ""),
    );
  },

  /** Print the result returned by a tool. */
  toolResult(toolName: string, result: string): void {
    const preview = result.length > 200 ? result.slice(0, 200) + "…" : result;
    console.log(
      `${label("Tool result", chalk.magenta)} (${chalk.bold(toolName)})\n` +
        `${" ".repeat(LABEL_WIDTH + 3)}${chalk.dim(preview)}`,
    );
  },

  /** Print an informational / status message. */
  info(text: string): void {
    console.log(chalk.blue(`ℹ  ${text}`));
  },

  /** Print a warning. */
  warn(text: string): void {
    console.warn(chalk.yellow(`⚠  ${text}`));
  },

  /** Print an error. */
  error(text: string): void {
    console.error(chalk.red(`✖  ${text}`));
  },

  /** Print the "press Enter to speak" prompt. */
  prompt(): void {
    process.stdout.write(chalk.cyan("\n🎤  Press Enter to speak (or type your message): "));
  },

  /** Print the "recording..." indicator. */
  recording(): void {
    process.stdout.write(chalk.red("⏺  Recording… (stop on silence or timeout)\n"));
  },

  /** Print the "transcribing..." indicator. */
  transcribing(): void {
    process.stdout.write(chalk.blue("📝  Transcribing speech…\n"));
  },

  /** Print a visual separator. */
  separator(): void {
    console.log(chalk.dim("─".repeat(60)));
  },

  /** Print the application banner. */
  banner(): void {
    console.log(chalk.bold.green("\n  ╔══════════════════════════════╗"));
    console.log(chalk.bold.green("  ║      H E R F A C E  MIC      ║"));
    console.log(chalk.bold.green("  ║  AI voice agent — heronwin   ║"));
    console.log(chalk.bold.green("  ╚══════════════════════════════╝\n"));
  },
};
