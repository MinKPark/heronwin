declare module "mic" {
  import type { Readable } from "stream";

  interface MicOptions {
    rate?: string;
    channels?: string;
    bitwidth?: string;
    encoding?: string;
    endian?: string;
    fileType?: string;
    device?: string;
    debug?: boolean;
    exitOnSilence?: number;
  }

  interface MicAudioStream extends Readable {
    on(event: "silence", listener: () => void): this;
    on(event: "sound", listener: () => void): this;
    on(event: "error", listener: (err: Error) => void): this;
    on(event: "startComplete", listener: () => void): this;
    on(event: "stopComplete", listener: () => void): this;
    on(event: "pauseComplete", listener: () => void): this;
    on(event: "resumeComplete", listener: () => void): this;
    on(event: string, listener: (...args: unknown[]) => void): this;
  }

  interface MicInstance {
    start(): void;
    stop(): void;
    pause(): void;
    resume(): void;
    getAudioStream(): MicAudioStream;
  }

  function mic(options?: MicOptions): MicInstance;
  export = mic;
}
