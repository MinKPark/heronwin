import { writeFile } from "fs/promises";
import { tmpdir } from "os";
import { join } from "path";
import { playWavFile } from "./playback.js";

const SAMPLE_RATE = 16_000;
const CHANNEL_COUNT = 1;
const BITS_PER_SAMPLE = 16;
const QUIET_BEEP_AMPLITUDE = 0.08;

let startCuePathPromise: Promise<string> | null = null;
let stopCuePathPromise: Promise<string> | null = null;

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

function createTonePcmBuffer(
  frequencyHz: number,
  durationMs: number,
  amplitude = QUIET_BEEP_AMPLITUDE,
): Buffer {
  const sampleCount = Math.max(1, Math.round((SAMPLE_RATE * durationMs) / 1000));
  const pcm = Buffer.alloc(sampleCount * 2);
  const maxAmplitude = Math.floor(32767 * amplitude);

  for (let i = 0; i < sampleCount; i += 1) {
    const angle = (2 * Math.PI * frequencyHz * i) / SAMPLE_RATE;
    const sample = Math.round(Math.sin(angle) * maxAmplitude);
    pcm.writeInt16LE(sample, i * 2);
  }

  return pcm;
}

function createSilencePcmBuffer(durationMs: number): Buffer {
  const sampleCount = Math.max(1, Math.round((SAMPLE_RATE * durationMs) / 1000));
  return Buffer.alloc(sampleCount * 2);
}

async function ensureCueFile(fileName: string, pcmData: Buffer): Promise<string> {
  const filePath = join(tmpdir(), fileName);
  await writeFile(filePath, createWavBuffer(pcmData));
  return filePath;
}

function getStartCuePath(): Promise<string> {
  startCuePathPromise ??= ensureCueFile(
    "herface-recording-start.wav",
    createTonePcmBuffer(880, 100),
  );
  return startCuePathPromise;
}

function getStopCuePath(): Promise<string> {
  stopCuePathPromise ??= ensureCueFile(
    "herface-recording-stop.wav",
    Buffer.concat([
      createTonePcmBuffer(660, 80),
      createSilencePcmBuffer(70),
      createTonePcmBuffer(880, 100),
    ]),
  );
  return stopCuePathPromise;
}

export async function playRecordingStartCue(): Promise<void> {
  if (process.platform !== "win32") {
    return;
  }

  await playWavFile(await getStartCuePath());
}

export async function playRecordingStopCue(): Promise<void> {
  if (process.platform !== "win32") {
    return;
  }

  await playWavFile(await getStopCuePath());
}
