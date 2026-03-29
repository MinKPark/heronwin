declare module "speech-recorder" {
  type AudioChunk = Buffer | ArrayBuffer | ArrayBufferView;

  interface AudioEvent {
    audio: AudioChunk;
    speaking?: boolean;
    speech?: boolean;
    probability?: number;
    volume?: number;
  }

  interface ChunkEvent {
    audio?: AudioChunk;
  }

  interface SpeechRecorderOptions {
    consecutiveFramesForSilence?: number;
    consecutiveFramesForSpeaking?: number;
    device?: number;
    leadingBufferFrames?: number;
    onAudio?: (event: AudioEvent) => void;
    onChunkEnd?: () => void;
    onChunkStart?: (event: ChunkEvent) => void;
    sampleRate?: number;
    samplesPerFrame?: number;
    sileroVadBufferSize?: number;
    sileroVadRateLimit?: number;
    sileroVadSilenceThreshold?: number;
    sileroVadSpeakingThreshold?: number;
    webrtcVadBufferSize?: number;
    webrtcVadLevel?: number;
    webrtcVadResultsSize?: number;
  }

  export class SpeechRecorder {
    constructor(options?: SpeechRecorderOptions);
    start(): void;
    stop(): void;
  }

  export function devices(): unknown[];
}
