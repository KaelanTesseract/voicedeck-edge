# Development

## How it works

```
Discord app  ⇄  Voicedeck Edge Hub (tray app, this PC only)  ⇄  iCUE widget on the XENEON EDGE
             named pipe (local RPC)           ws://localhost:47820
```

iCUE widgets cannot talk to Discord directly, so a small tray app (the *hub*) connects to the Discord desktop app's local RPC interface and offers the widget a WebSocket on `localhost:47820`.

- Discord only grants RPC scopes to the owner of a Discord app (plus up to 50 testers), so every user creates their own app. Sign-in uses OAuth2 with PKCE (public client) – no client secret.
- Widgets are paired once on the PC (“Allow widget?”); web pages (`http(s)://` origins) and non-loopback requests are refused.
- Avatars, server icons, server emojis and chat images are fetched by the hub from Discord's CDN only and served from memory via opaque paths – no open URL proxy.
- Direct messages are discovered through `NOTIFICATION_CREATE` (RPC has no DM list) and kept in memory only.

Full design document (German): [DESIGN.md](DESIGN.md)

## Repository layout

| Folder | Content |
|---|---|
| `hub/` | Tray app (C#/.NET 8, WinForms): Discord RPC, WebSocket server, pairing, setup assistant |
| `hub.Tests/` | xUnit tests |
| `widget/` | iCUE widget (HTML/CSS/JS, Widget API 1.9) |
| `installer/` | Inno Setup script |
| `tools/` | `hub-probe.mjs`, `voice-test.mjs` (end-to-end checks), screenshot and icon generators |
| `spike/` | The RPC exploration programs the design is based on |
| `docs/` | Setup guide, design document, release notes, images |

## Build

Requires .NET 8 SDK, Node.js (for `icuewidget-cli`) and Inno Setup 6.

```bash
npm install -g icuewidget-cli
dotnet test hub.Tests
dotnet publish hub -c Release -o artifacts/hub
icuewidget package widget -o artifacts/VoicedeckEdge.icuewidget
iscc /DAppVersion=0.9.0 installer/VoicedeckEdge.iss
```

Releases are built by GitHub Actions: pushing a tag `v<version>` runs the tests, builds hub, widget and installer and attaches them to a draft release. Hub (`hub/VoicedeckEdge.Hub.csproj`) and widget (`widget/manifest.json`) must carry the tag's version.

## Testing in a browser

- Serve `widget/` locally, e.g. `python -m http.server 5510 --bind 127.0.0.1 --directory widget`.
- `index.html?demo` shows sample data (fictional people). Extra demo options: `lang=en|de`, `tab=voice|channels|chat`, `theme=dark|light`, `private=1`, `guild=dms`, `popup=<name>`, `layout=list`.
- To talk to the real hub from a browser page, start the hub with `--dev` (allows `http://localhost` origins).
- `node tools/hub-probe.mjs <tokenFile>` and `node tools/voice-test.mjs <tokenFile>` exercise the hub end to end.

## Screenshots

`bash tools/screenshots.sh` renders the widget with demo data in the real slot sizes (headless Microsoft Edge), `python tools/make-hero.py` builds the hero image, and a debug build of the hub renders the setup assistant: `VoicedeckEdgeHub.exe --render-wizard <dir> <de|en>`.
