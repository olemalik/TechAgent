# @olemalik/techagent-chat

An embeddable Angular chat component that connects to a **TechAgent Oil & Gas AI API**. Drop it into any Angular 19+ app with a single component tag — no extra configuration required beyond your API URL.

---

## Features

- **Zero-config layout** — full-height chat panel with messages area, input bar, and file attachment support, all styled out of the box
- **Streaming responses** — AI answers stream token-by-token in real time using SSE
- **File / image attachments** — drag-and-drop or paperclip upload; images preview inline, other files show as download links
- **Conversation history** — loads previous session messages on mount; persists across page refreshes via session ID
- **Feedback (thumbs up / down)** — each AI answer can be rated; positive ratings become golden training examples
- **Typing indicator** — animated dots while the AI is generating
- **Welcome screen** — shown when the conversation is empty; disappears once messages arrive
- **Syncfusion Chat UI** — built on `ejs-chatui` for message rendering; Syncfusion CSS is auto-injected from CDN — no stylesheet import needed in the consumer app
- **InjectionToken config** — API URL and Syncfusion license key are passed in via Angular's DI, not hardcoded

---

## Installation

```bash
npm install @olemalik/techagent-chat
```

Syncfusion packages are bundled as dependencies and install automatically.

---

## Usage

### 1. Provide the API URL (and optional Syncfusion license) in `app.config.ts`

```ts
import { ApplicationConfig } from '@angular/core';
import { TECHAGENT_API_URL, SYNCFUSION_LICENSE_KEY } from '@olemalik/techagent-chat';

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: TECHAGENT_API_URL,       useValue: 'https://your-api-url.com' },
    { provide: SYNCFUSION_LICENSE_KEY,  useValue: 'YOUR_SYNCFUSION_LICENSE_KEY' }, // optional
  ]
};
```

> If you omit `SYNCFUSION_LICENSE_KEY`, the component works but shows a Syncfusion trial banner. The default for `TECHAGENT_API_URL` is `http://localhost:5073`.

### 2. Add the component to your template

```ts
// your-page.component.ts
import { TechAgentChatComponent } from '@olemalik/techagent-chat';

@Component({
  standalone: true,
  imports: [TechAgentChatComponent],
  template: `
    <div style="height: 100vh;">
      <techagent-chat />
    </div>
  `
})
export class YourPageComponent {}
```

> The `<techagent-chat>` element needs a parent with a defined height. It fills 100% of whatever height you give it, with a minimum of 500 px as a fallback.

### 3. Optional — listen for new session events

```html
<techagent-chat (sessionCreated)="onSession($event)" />
```

```ts
onSession(sessionId: string) {
  console.log('New chat session started:', sessionId);
}
```

---

## Exported tokens

| Token | Type | Default | Purpose |
|---|---|---|---|
| `TECHAGENT_API_URL` | `InjectionToken<string>` | `http://localhost:5073` | Base URL of the TechAgent API |
| `SYNCFUSION_LICENSE_KEY` | `InjectionToken<string>` | `''` | Syncfusion license (suppresses trial banner) |

---

## Building the package locally

All commands run from the **Angular workspace root** (`TechAgent.Client/`).

```bash
# 1. Build the library
npx ng build techagent-chat

# Output goes to:  dist/techagent-chat/
```

---

## Publishing to npm

### Step 1 — bump the version

Edit `projects/techagent-chat/package.json` and increment the version:

```json
"version": "1.0.4"
```

Follow [semver](https://semver.org/):
- **patch** (`1.0.x`) — bug fixes, style tweaks
- **minor** (`1.x.0`) — new features, backwards-compatible
- **major** (`x.0.0`) — breaking changes

### Step 2 — rebuild

```bash
npx ng build techagent-chat
```

### Step 3 — publish (requires npm 2FA OTP)

```bash
cd dist/techagent-chat && npm publish --access public --otp=YOUR_6_DIGIT_CODE
```

> Get the OTP from your authenticator app (same one linked to your npm account `olemalik@gmail.com`). The code expires in ~30 seconds, so run the command immediately after copying it.

### Step 4 — verify it is live

```bash
npm view @olemalik/techagent-chat version
```

---

## Updating a consumer app to the latest version

In the app that has the package installed:

```bash
npm install @olemalik/techagent-chat@latest
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Layout broken / messages and input separated | Parent element has no height | Wrap `<techagent-chat>` in a `div` with `height: 100vh` or a fixed height |
| Syncfusion trial banner | No license key provided | Pass your key via `SYNCFUSION_LICENSE_KEY` token |
| `E404` on publish | Version already published | Bump version in `package.json` and rebuild |
| `EOTP` on publish | 2FA required | Add `--otp=YOUR_CODE` to the publish command |
| `uv_cwd` terminal error | Terminal's working directory was deleted | Use the full absolute path: `cd /full/path/to/dist/techagent-chat && npm publish ...` |
| Syncfusion input ("Type your message…") visible | Old version before 1.0.3 | Update to `@olemalik/techagent-chat@latest` |
