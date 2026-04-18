import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { exec, spawn } from "child_process";
import { promisify } from "util";
import { z } from "zod";

const execAsync = promisify(exec);

const server = new McpServer({
  name: "process-manager",
  version: "1.0.0",
});

server.tool(
  "list_processes",
  "List running processes on the local machine",
  {},
  async () => {
    const isWindows = process.platform === "win32";
    const cmd = isWindows ? "tasklist /FO CSV /NH" : "ps aux --no-headers";
    try {
      const { stdout } = await execAsync(cmd);
      return {
        content: [{ type: "text" as const, text: stdout.trim() }],
      };
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return {
        content: [{ type: "text" as const, text: `Failed to list processes: ${message}` }],
        isError: true,
      };
    }
  },
);

server.tool(
  "start_process",
  "Start a new process on the local machine",
  {
    command: z.string().describe("The executable command to run"),
    args: z.array(z.string()).optional().describe("Command-line arguments for the process"),
    cwd: z.string().optional().describe("Working directory for the process"),
  },
  async ({ command, args, cwd }) => {
    try {
      const child = spawn(command, args ?? [], {
        cwd,
        detached: true,
        stdio: "ignore",
        shell: false,
      });
      child.unref();

      const pid = child.pid;
      if (pid === undefined) {
        return {
          content: [{ type: "text" as const, text: "Failed to start process: no PID assigned" }],
          isError: true,
        };
      }

      return {
        content: [
          {
            type: "text" as const,
            text: `Process started successfully with PID: ${pid}`,
          },
        ],
      };
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return {
        content: [{ type: "text" as const, text: `Failed to start process: ${message}` }],
        isError: true,
      };
    }
  },
);

server.tool(
  "stop_process",
  "Stop a running process by its PID",
  {
    pid: z.number().int().positive().describe("Process ID (PID) of the process to stop"),
    force: z
      .boolean()
      .optional()
      .describe("Force-kill the process using SIGKILL (default: false, uses SIGTERM)"),
  },
  async ({ pid, force }) => {
    try {
      const signal = force === true ? "SIGKILL" : "SIGTERM";
      process.kill(pid, signal);
      return {
        content: [
          {
            type: "text" as const,
            text: `Sent ${signal} to process ${pid}`,
          },
        ],
      };
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      return {
        content: [
          {
            type: "text" as const,
            text: `Failed to stop process ${pid}: ${message}`,
          },
        ],
        isError: true,
      };
    }
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
