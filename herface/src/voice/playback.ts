import { spawn } from "child_process";

function escapePowerShellSingleQuotedString(value: string): string {
  return value.replaceAll("'", "''");
}

function buildPlaybackScript(filePath: string): string {
  const escapedPath = escapePowerShellSingleQuotedString(filePath);

  return [
    "$ErrorActionPreference = 'Stop'",
    "$player = New-Object System.Media.SoundPlayer",
    `$player.SoundLocation = '${escapedPath}'`,
    "$player.Load()",
    "$player.PlaySync()",
  ].join("; ");
}

/**
 * Replay a WAV file locally for debugging. This resolves when playback finishes.
 */
export function playWavFile(filePath: string): Promise<void> {
  if (process.platform !== "win32") {
    return Promise.reject(
      new Error("Debug audio playback is only supported on Windows in this build."),
    );
  }

  return new Promise((resolve, reject) => {
    const child = spawn(
      "powershell.exe",
      ["-NoProfile", "-NonInteractive", "-Command", buildPlaybackScript(filePath)],
      {
        stdio: ["ignore", "ignore", "pipe"],
        windowsHide: true,
      },
    );

    let stderr = "";
    child.stderr.on("data", (chunk: Buffer | string) => {
      stderr += chunk.toString();
    });

    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(
        new Error(
          stderr.trim() || `Audio playback process exited with code ${code ?? "unknown"}.`,
        ),
      );
    });
  });
}
