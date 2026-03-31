import { unlink, writeFile } from "fs/promises";
import { tmpdir } from "os";
import { join } from "path";
import { SpeechRecorder } from "speech-recorder";

export interface RecordingResult {
  filePath: string;
  /** Delete the temporary file once you are done with it. */
  cleanup: () => Promise<void>;
}

const SAMPLE_RATE = 16_000;
const CHANNEL_COUNT = 1;
const BITS_PER_SAMPLE = 16;
const SILENCE_GRACE_MS = 1_500;
const STOP_FLUSH_MS = 250;
const CONSECUTIVE_FRAMES_FOR_SPEAKING = 2;
const CONSECUTIVE_FRAMES_FOR_SILENCE = 20;
const LEADING_BUFFER_FRAMES = 15;
const WEBRTC_VAD_LEVEL = 2;

export const RECORDING_FORMAT = {
  sampleRateHz: SAMPLE_RATE,
  channelCount: CHANNEL_COUNT,
  bitsPerSample: BITS_PER_SAMPLE,
} as const;

export function describeRecordingFormat(): string {
  return `${SAMPLE_RATE} Hz, ${CHANNEL_COUNT} channel, ${BITS_PER_SAMPLE}-bit PCM`;
}

function ensureWindowsSupported(): void {
  if (process.platform !== "win32") {
    throw new Error("This build records directly from the microphone on Windows only.");
  }
}

function toBuffer(chunk: Buffer | ArrayBuffer | ArrayBufferView): Buffer {
  if (Buffer.isBuffer(chunk)) {
    return chunk;
  }

  if (ArrayBuffer.isView(chunk)) {
    return Buffer.from(chunk.buffer, chunk.byteOffset, chunk.byteLength);
  }

  return Buffer.from(chunk);
}

function createWavBuffer(pcmData: Buffer): Buffer {
  const blockAlign = (CHANNEL_COUNT * BITS_PER_SAMPLE) / 8;
  const byteRate = SAMPLE_RATE * blockAlign;
  const header = Buffer.alloc(44);

  header.write("RIFF", 0);
  header.writeUInt32LE(36 + pcmData.length, 4);
  header.write("WAVE", 8);
  header.write("fmt ", 12);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(CHANNEL_COUNT, 22);
  header.writeUInt32LE(SAMPLE_RATE, 24);
  header.writeUInt32LE(byteRate, 28);
  header.writeUInt16LE(blockAlign, 32);
  header.writeUInt16LE(BITS_PER_SAMPLE, 34);
  header.write("data", 36);
  header.writeUInt32LE(pcmData.length, 40);

  return Buffer.concat([header, pcmData]);
}

/**
 * Record audio from the default Windows microphone until silence is detected or
 * `maxDurationMs` elapses, whichever comes first.
 * The output is saved as a temporary WAV file for Whisper transcription.
 * Returns the path to a temporary WAV file containing the recorded audio.
 */
export function recordAudio(maxDurationMs = 30_000): Promise<RecordingResult> {
  ensureWindowsSupported();

  return new Promise((resolve, reject) => {
    const filePath = join(tmpdir(), `herface-${Date.now()}.wav`);
    const pcmChunks: Buffer[] = [];
    let settled = false;
    let stopRequested = false;
    let speechDetected = false;
    let silenceTimer: NodeJS.Timeout | null = null;
    let finalizeTimer: NodeJS.Timeout | null = null;

    let recorder!: SpeechRecorder;

    const cleanupTempFile = async (): Promise<void> => {
      await unlink(filePath).catch(() => undefined);
    };

    const fail = async (error: unknown): Promise<void> => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeout);
      if (silenceTimer) {
        clearTimeout(silenceTimer);
      }
      if (finalizeTimer) {
        clearTimeout(finalizeTimer);
      }

      try {
        recorder.stop();
      } catch {
        // Ignore stop failures once we're already unwinding.
      }

      await cleanupTempFile();
      reject(error instanceof Error ? error : new Error(String(error)));
    };

    const finalize = async (): Promise<void> => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeout);
      if (silenceTimer) {
        clearTimeout(silenceTimer);
      }
      if (finalizeTimer) {
        clearTimeout(finalizeTimer);
      }

      try {
        const pcmData = Buffer.concat(pcmChunks);
        if (pcmData.length === 0) {
          throw new Error(
            speechDetected
              ? "No audio was captured from the microphone."
              : "No speech was detected before recording stopped.",
          );
        }

        await writeFile(filePath, createWavBuffer(pcmData));
        resolve({
          filePath,
          cleanup: () => unlink(filePath).catch(() => undefined),
        });
      } catch (error) {
        await cleanupTempFile();
        reject(error instanceof Error ? error : new Error(String(error)));
      }
    };

    const stopAndFinalize = (): void => {
      if (stopRequested || settled) {
        return;
      }

      stopRequested = true;
      if (silenceTimer) {
        clearTimeout(silenceTimer);
        silenceTimer = null;
      }

      try {
        recorder.stop();
      } catch (error) {
        void fail(error);
        return;
      }

      finalizeTimer = setTimeout(() => {
        void finalize();
      }, STOP_FLUSH_MS);
    };

    const timeout = setTimeout(() => {
      stopAndFinalize();
    }, maxDurationMs);

    try {
      recorder = new SpeechRecorder({
        sampleRate: SAMPLE_RATE,
        device: -1,
        consecutiveFramesForSilence: CONSECUTIVE_FRAMES_FOR_SILENCE,
        consecutiveFramesForSpeaking: CONSECUTIVE_FRAMES_FOR_SPEAKING,
        leadingBufferFrames: LEADING_BUFFER_FRAMES,
        webrtcVadLevel: WEBRTC_VAD_LEVEL,
        onAudio: ({ audio, speaking, speech }) => {
          pcmChunks.push(toBuffer(audio));

          const isSpeechFrame = speaking ?? speech ?? false;
          if (isSpeechFrame) {
            speechDetected = true;
            if (silenceTimer) {
              clearTimeout(silenceTimer);
              silenceTimer = null;
            }
            return;
          }

          if (speechDetected && !silenceTimer) {
            silenceTimer = setTimeout(() => {
              stopAndFinalize();
            }, SILENCE_GRACE_MS);
          }
        },
        onChunkStart: () => {
          speechDetected = true;
          if (silenceTimer) {
            clearTimeout(silenceTimer);
            silenceTimer = null;
          }
        },
        onChunkEnd: () => {
          if (speechDetected && !silenceTimer) {
            silenceTimer = setTimeout(() => {
              stopAndFinalize();
            }, SILENCE_GRACE_MS);
          }
        },
      });

      recorder.start();
    } catch (error) {
      void fail(error);
    }
  });
}
