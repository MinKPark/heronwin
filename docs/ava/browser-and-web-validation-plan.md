# Browser And Web Validation Plan

## Goal

AVA should detect when the driven app is a browser or Chromium-based app, then apply the right validation scopes:

- Windows UIA validation for the whole window and host shell.
- Web/W3C validation for the web content only when Chrome DevTools Protocol (CDP) evidence is available.

This avoids treating browser chrome as web content, while still checking the actual page or app document with web-specific rules when AVA has real browser evidence.

## Problem

Browser and Chromium app UIA trees mix multiple surfaces in one tree:

- Browser shell: tabs, address bar, toolbar, favorites bar, profile controls.
- Native host shell: Electron/CEF window frame and app chrome.
- Web content: the document subtree, usually exposed through `Document` and `RootWebArea`.

Applying one profile to the whole tree creates noisy findings. A Windows UIA profile can flag web wrapper nodes as generic groups. A web profile can accidentally inspect browser toolbar controls as if they were page content.

## Detection Model

Introduce an app surface classifier that runs on captured window evidence before validation.

### App Kind

Use a confidence-based classification:

| Kind | Description | Example signals |
| --- | --- | --- |
| `windows-native` | Native Windows app with no clear web content subtree. | No `RootWebArea`, native control classes, non-browser process. |
| `browser` | General browser window. | Process name like `msedge` or `chrome`; window class `Chrome_WidgetWin_1`; browser classes like `BrowserRootView`, `EdgeToolbarView`, `OmniboxViewViews`, tab strip classes. |
| `chromium-app` | App-hosted Chromium/Electron/CEF surface. | Window class `Chrome_WidgetWin_1`; app-specific process; `Document` or `RootWebArea`; no address bar/tab strip browser chrome. |
| `unknown-web-host` | Web content detected, but host cannot be confidently identified. | `Document` or `RootWebArea` exists, but process/class signals are ambiguous. |

### Web Content Detection

Treat these as strong signals for web content:

- UIA `controlType`: `Document`.
- `automationId`: `RootWebArea`.
- Browser-derived page roles/classes under a `Document` subtree.

The classifier should return:

```json
{
  "appKind": "browser",
  "confidence": "high",
  "webContentDetected": true,
  "webRootPaths": ["1/0/0/1/1/0/0/0/0/0/0"],
  "hostShellPaths": ["root"],
  "signals": ["process: msedge", "className: Chrome_WidgetWin_1", "document: RootWebArea"]
}
```

## Validation Strategy

### Windows UIA Scope

Run Windows UIA rules against the full window, but keep the browser/noise filters we have been adding:

- Ignore generic non-focusable container groups that only expose wrapper-like `invoke`.
- Ignore focus proxy groups with only structural actions when they do not represent a distinct visible control.
- Keep real browser shell controls in scope: tabs, toolbar buttons, address bar, app/menu buttons.

### Web/W3C Scope

Run web rules only when CDP is available for the target browser or Chromium app.

When CDP is available, capture and validate:

- DOM snapshot evidence.
- Chromium accessibility tree evidence.
- Current document HTML as reviewer reference.

Use CDP-backed data for web/W3C rules:

- DOM accessibility tree.
- ARIA attributes and computed roles/names.
- Axe-style rule results when available.

If CDP is not available, do not run web/W3C validation for that checkpoint. AVA should still run Windows UIA validation and record that web validation was skipped because CDP evidence was unavailable.

## Report Model

Add surface metadata to reports and evidence:

- `appKind`
- `webContentDetected`
- `validationScopes`
- `surface` per finding: `host-shell`, `browser-chrome`, `web-content`, or `unknown`
- `webRootPath` when applicable
- `htmlReference` when CDP saved document HTML

Example finding metadata:

```json
{
  "surface": "web-content",
  "profileId": "federal-web-min",
  "ruleId": "WEB-WCAG-4.1.2-NAME",
  "webRootPath": "1/0/0/1/1/0/0/0/0/0/0",
  "htmlReference": "evidence/step-005/web/001-page.html"
}
```

In Markdown, keep the main finding table compact. Surface can be a column only if it proves useful; otherwise include it in JSON first and summarize counts by surface in the report summary. When CDP captures HTML, the step evidence section should include a generated report link such as `html` pointing to `evidence/step-005/web/001-page.html`.

## Configuration

Add validation config options:

```yaml
profile: federal-windows-uia-min
webValidation:
  mode: auto
  profile: federal-web-min
  evidence: cdp
```

Modes:

- `off`: Windows UIA only.
- `auto`: run web validation only when web content is detected and CDP evidence is available.
- `required`: fail/not-tested if CDP evidence cannot be captured for detected web content.

Evidence mode:

- `cdp`: use Chrome DevTools Protocol. No UIA-subtree fallback for web/W3C validation.

CDP discovery:

- Default endpoint: `http://127.0.0.1:9222`.
- Override endpoint with `AVA_CDP_ENDPOINT`.
- Override port with `AVA_CDP_PORT`.
- Browsers and Chromium apps must be launched with a remote debugging endpoint, for example `--remote-debugging-port=9222`.

## Implementation Phases

### Phase 1: UIA-Based Surface Classification

- Add a classifier that reads captured `describe_window` evidence.
- Identify browser chrome and web root paths.
- Store classification in report JSON.
- Add unit tests with Edge browser, Chromium app, native app, and ambiguous fixtures.

### Phase 2: CDP Evidence Collection

- Detect whether CDP is available for the target browser or Chromium app.
- Match the active tab/page target using title, URL, window, and web root signals.
- Capture DOM snapshot evidence.
- Capture Chromium accessibility tree evidence.
- Save current HTML content as `evidence/step-XXX/web/NNN-page.html`.
- Add the saved HTML link to the step evidence section and web finding evidence.

### Phase 3: Scoped Validators

- Split deterministic validation into scopes:
  - full-window Windows UIA scope
  - web-content scope
- Apply `federal-windows-uia-min` to the full tree.
- Apply `federal-web-min` only to CDP DOM/accessibility evidence when enabled and available.
- Deduplicate across scopes only when findings point to the same real element and same rule intent.

### Phase 4: Report Improvements

- Add surface metadata to finding JSON.
- Add optional summary counts by surface/profile.
- Keep evidence links and highlighted screenshots working for both scopes.
- Include saved HTML reference links for CDP-backed web validation.
- Make rule links continue to resolve to `docs/ava/rules`.

### Phase 5: Optional Browser Protocol Expansion

- Add optional WebDriver support if it provides comparable DOM/accessibility evidence.
- Keep CDP as the initial and preferred implementation for Chromium and Edge.

## Acceptance Criteria

- Browser chrome controls are validated as Windows UIA, not web content.
- Web document descendants are validated with web/W3C rules only when CDP evidence is available.
- Chromium apps without browser toolbar are classified separately from full browsers.
- Native Windows apps do not run web rules unless a real web root is detected and CDP evidence is available.
- Reports show which profile/surface produced each finding.
- CDP-backed steps save the current HTML content and link to it from the report.
- Regenerating a report from saved evidence produces the same classification and scoped findings.

## Open Questions

- Should browser shell findings be grouped separately from app-content findings in Markdown?
- Should web validation run by default for `federal-windows-uia-min`, or require an explicit `webValidation` block?
- How should AVA handle multiple web roots in one window?
- Should AVA launch managed browser sessions with a debugging port when CDP is required, or only attach when the current app already exposes CDP?
