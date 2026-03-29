import mic from "mic";
import { createWriteStream } from "fs";
import { unlink } from "fs/promises";
import { tmpdir } from "os";
import { join } from "path";

export interface RecordingResult {
  filePath: string;
  /** Delete the temporary file once you are done with it. */
  cleanup: () => Promise<void>;
}

/**
 * Record audio from the default microphone until silence is detected or
 * `maxDurationMs` elapses, whichever comes first.
 *
 * Prerequisites (must be available in PATH):
 *   - Linux/macOS: `arecord` (ALSA) or `rec` (SoX)
 *   - Windows:     `sox` (http://sox.sourceforge.net)
 *
 * Returns the path to a temporary WAV file containing the recorded audio.
 */
export function recordAudio(maxDurationMs = 30_000): Promise<RecordingResult> {
  return new Promise((resolve, reject) => {
    const filePath = join(tmpdir(), `herface-${Date.now()}.wav`);
    const outputStream = createWriteStream(filePath);

    const micInstance = mic({
      rate: "16000",
      channels: "1",
      bitwidth: "16",
      encoding: "signed-integer",
      endian: "little",
      fileType: "wav",
      exitOnSilence: 6,
    });

    const audioStream = micInstance.getAudioStream();

    audioStream.on("silence", () => {
      micInstance.stop();
    });

    audioStream.on("error", (err: Error) => {
      micInstance.stop();
      reject(err);
    });

    outputStream.on("error", (err: Error) => {
      micInstance.stop();
      reject(err);
    });

    outputStream.on("finish", () => {
      resolve({
        filePath,
        cleanup: () => unlink(filePath).catch(() => undefined),
      });
    });

    audioStream.pipe(outputStream);
    micInstance.start();

    // Safety timeout — stop recording after maxDurationMs regardless
    setTimeout(() => {
      micInstance.stop();
    }, maxDurationMs);
  });
}
