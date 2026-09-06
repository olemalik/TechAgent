# TechAgent Client — CLAUDE.md

Guidelines for Claude Code when working in this Angular workspace.

---

## Permission Rule

**ALWAYS ask the user for explicit permission before taking any of the following actions:**

- Editing any file
- Deleting any file or data
- Running or executing any command, script, or process
- Any other action that modifies state (build artifacts, config, git, npm, etc.)

Wait for a clear confirmation (yes / no / ok / not ok) before proceeding. Do not assume consent from context alone.

---

## Critical Rule — Keep App and Package in Sync

The chat feature exists in **two places** that must always be identical in logic and templates:

| App (source of truth) | npm Package (mirror) |
|---|---|
| `src/app/chat/chat.component.ts` | `projects/techagent-chat/src/lib/chat/chat.component.ts` |
| `src/app/chat/chat.component.html` | `projects/techagent-chat/src/lib/chat/chat.component.html` |
| `src/app/chat/chat.component.css` | `projects/techagent-chat/src/lib/chat/chat.component.css` |
| `src/app/chat/chat.service.ts` | `projects/techagent-chat/src/lib/chat/chat.service.ts` |
| `src/app/chat/chat-input/chat-input.component.ts` | `projects/techagent-chat/src/lib/chat/chat-input/chat-input.component.ts` |
| `src/app/chat/chat-input/chat-input.component.html` | `projects/techagent-chat/src/lib/chat/chat-input/chat-input.component.html` |
| `src/app/chat/chat-input/chat-input.component.css` | `projects/techagent-chat/src/lib/chat/chat-input/chat-input.component.css` |
| `src/app/chat/chat-messages/chat-messages.component.ts` | `projects/techagent-chat/src/lib/chat/chat-messages/chat-messages.component.ts` |
| `src/app/chat/chat-messages/chat-messages.component.html` | `projects/techagent-chat/src/lib/chat/chat-messages/chat-messages.component.html` |
| `src/app/chat/chat-messages/chat-messages.component.css` | `projects/techagent-chat/src/lib/chat/chat-messages/chat-messages.component.css` |
| `src/app/chat/models/chat.model.ts` | `projects/techagent-chat/src/lib/chat/models/chat.model.ts` |
| `src/app/shared/file-uploader/file-uploader.component.ts` | `projects/techagent-chat/src/lib/shared/file-uploader/file-uploader.component.ts` |
| `src/app/shared/file-uploader/file-uploader.component.html` | `projects/techagent-chat/src/lib/shared/file-uploader/file-uploader.component.html` |
| `src/app/shared/file-uploader/file-uploader.component.css` | `projects/techagent-chat/src/lib/shared/file-uploader/file-uploader.component.css` |

**Whenever you edit any file in `src/app/chat/` or `src/app/shared/file-uploader/`, you MUST apply the same change to its counterpart in `projects/techagent-chat/src/lib/` in the same response — never leave them out of sync.**

### Known differences between app and package versions

The package versions are NOT byte-for-byte identical to the app versions. These intentional differences must be preserved when syncing:

1. **API URL** — the app uses `environment.apiUrl`; the package uses `inject(TECHAGENT_API_URL)` from `../../tokens/api-url.token`
2. **Syncfusion license** — the package's `chat.component.ts` injects `SYNCFUSION_LICENSE_KEY` and calls `registerLicense()` in the constructor; the app does not
3. **CSS variables** — the package's `chat.component.css` defines all `--chat-bg`, `--footer-bg`, `--footer-border`, `--welcome-icon`, etc. variables on the `techagent-chat {}` selector with concrete default values; the app relies on global CSS variables defined in `styles.css`
4. **Syncfusion CDN link** — the package's `chat.component.ts` constructor injects a `<link>` to the Syncfusion CDN stylesheet via `DOCUMENT`; the app imports the CSS directly
5. **`showFooter="false"`** — the package's `chat-messages.component.html` has `[showFooter]="false"` on `ejs-chatui` to hide the built-in input; check the app version does not need this
6. **CSS fallback values** — the package's CSS uses `var(--token, #fallback)` form throughout; the app CSS uses bare `var(--token)`

When syncing a change, carry the logic/template change across but preserve these differences on the package side.

---

## Build and Publish the npm Package

Run all commands from the **workspace root** (`TechAgent.Client/`).

### Build

```bash
npx ng build techagent-chat
# Output: dist/techagent-chat/
```

### Publish (requires npm 2FA OTP)

```bash
# 1. Bump version in projects/techagent-chat/package.json  (semver: patch / minor / major)
# 2. Rebuild
npx ng build techagent-chat
# 3. Publish — get OTP from authenticator app, enter immediately (expires in ~30 s)
cd dist/techagent-chat && npm publish --access public --otp=YOUR_6_DIGIT_CODE
# 4. Verify
npm view @olemalik/techagent-chat version
```

### Update a consumer app

```bash
npm install @olemalik/techagent-chat@latest
```

---

## Commands

```bash
# Serve the app locally
npx ng serve

# Build the app
npx ng build

# Build only the library
npx ng build techagent-chat

# Run tests
npx ng test
```

---

## Project Structure

```
TechAgent.Client/
├── src/app/
│   ├── chat/                        # Main app chat feature (source of truth)
│   │   ├── chat.component.*
│   │   ├── chat.service.ts
│   │   ├── chat-input/
│   │   ├── chat-messages/
│   │   └── models/
│   ├── shared/file-uploader/        # Shared uploader (also in package)
│   ├── sidebar/                     # App-only (session list, nav)
│   ├── documents/                   # App-only (document management)
│   └── settings/                    # App-only (MCP server config)
│
└── projects/techagent-chat/         # npm package — mirror of chat feature
    └── src/lib/
        ├── chat/
        ├── shared/file-uploader/
        └── tokens/api-url.token.ts  # Package-only (InjectionTokens)
```

`sidebar/`, `documents/`, and `settings/` are **app-only** — do not add them to the package.