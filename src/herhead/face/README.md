# face

Native Windows companion UI for `heronwin`.

`face` is a WPF desktop shell intended to sit alongside `brain` and show live agent state such as standby, listening, transcribing, thinking, acting, speaking, and error.

## Current scope

- floating always-on-top companion window
- simple mascot-style visual state changes
- recent activity log
- tray icon with open, settings, and exit
- settings window that edits `brain` `.env` values and stores local face settings
- named-pipe client that reconnects to `brain` automatically

## Runtime contract

By default `face` listens on the named pipe `heronwin.face`.

`brain` can publish JSON lines shaped like this:

```json
{
  "state": "thinking",
  "headline": "Thinking",
  "detail": "Planning the next move.",
  "transcript": null,
  "toolName": null,
  "timestampUtc": "2026-04-05T21:40:00Z"
}
```

The settings window writes `FACE_PIPE_ENABLED=true` and `FACE_PIPE_NAME=...` into the selected `.env` file so the runtime and UI can stay aligned.

## Run

```powershell
dotnet run --project .\src\herhead\face\Face.csproj
```

If `brain` is not running yet, `face` stays online and can cycle a demo state flow from the main window.