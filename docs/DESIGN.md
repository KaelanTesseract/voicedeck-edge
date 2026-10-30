# Voicedeck Edge – Design-Entwurf

Stand: 2026-10-27 · Status: v2 freigegeben · Schritte 1–7 umgesetzt (Release 0.9.0 lokal fertig, noch nicht veröffentlicht) · (Spike, Hub, Assistent + Daten löschen, Widget komplett inkl. Hell/Dunkel, Hintergrundbild, Textgröße)

Open-Source-Discord-Steuerung für die CORSAIR XENEON EDGE. Besteht aus zwei Teilen:

| Teil | Technik | Aufgabe |
|---|---|---|
| **Voicedeck Edge Hub** | C# / .NET 8, WinForms-Tray-App | Spricht mit dem Discord-Desktop-Client (lokale RPC über Named Pipe `\\.\pipe\discord-ipc-0..9`), stellt dem Widget einen lokalen WebSocket bereit |
| **Voicedeck Edge Widget** | iCUE-HTML-Widget (Widget API 1.9.0) | Touch-Oberfläche auf der Xeneon Edge |

Eigene Implementierung, kein Code und kein Branding von Deckord.

**Leitlinien:** 1. So einfach wie möglich für alle Anwender (kein Tippen von Codes, keine Ports, keine Runtime-Installation). 2. Datenschutz by Design: keine eigenen Server, keine Telemetrie, Chat-Inhalte nie auf der Festplatte (siehe Abschnitt 3).

---

## 1. Architektur

```
┌──────────────┐  Named Pipe (RPC)  ┌─────────────────────┐  ws://localhost:47820  ┌──────────────┐
│ Discord App  │ ◄────────────────► │ Voicedeck Edge Hub  │ ◄────────────────────► │ iCUE-Widget  │
│ (Desktop)    │                    │ (Tray, 127.0.0.1)   │  http://localhost:47820│ Xeneon Edge  │
└──────────────┘                    └─────────┬───────────┘  /img/... (Avatare)    └──────────────┘
                                              │ HTTPS (einmalig + Refresh)
                                              ▼
                                   discord.com/api/oauth2/token
```

- Der Hub hält **eine** RPC-Verbindung, abonniert Events und schickt dem Widget fertige **Zustands-Snapshots + Deltas** (JSON). Das Widget enthält keine Discord-Logik.
- **Avatare/Server-Icons** lädt der Hub von `cdn.discordapp.com`, cached sie und liefert sie unter `http://localhost:47820/img/...` aus. Dadurch braucht das Widget **nur eine einzige Berechtigung** (localhost).
- Der Server lauscht **nur auf 127.0.0.1**, nie im Netzwerk.

### 1.1 Discord-Anbindung (Hub)

- Jeder Nutzer braucht eine **eigene Discord-Application**. Grund: Die RPC-Scopes sind ohne Discord-Prüfung nur für den App-Besitzer und bis zu 50 Tester freigegeben; eine gemeinsame App für alle ist deshalb nicht möglich. Diesen Schritt macht der Einrichtungs-Assistent so leicht wie möglich (siehe 1.5).
- **Nur die Client-ID nötig (im Spike bestätigt, 2026-10-27).** Die Application wird als *Public Client* angelegt (Schalter unter OAuth2), `AUTHORIZE` bekommt `code_challenge` + `code_challenge_method: S256`, der Token-Tausch nutzt `code_verifier` ohne Secret. Antwort enthält `access_token`, `refresh_token`, `expires_in`. Kein Client-Secret für Nutzer und Hub.
- Ablauf: `HANDSHAKE` → `AUTHORIZE` (Discord zeigt einmalig einen Zustimmungsdialog) → Code-Tausch gegen Token bei `discord.com/api/oauth2/token` → `AUTHENTICATE`. Refresh-Token wird gespeichert, danach kein Dialog mehr.
- Scopes: genau `rpc`, `rpc.voice.read`, `rpc.voice.write`, `messages.read` (im Spike bestätigt). `identify`/`guilds` sind **nicht** nötig: `GET_GUILDS` funktioniert ohne, und der eigene Nutzer kommt schon im `READY`-Event.
- **Zugangsdaten** (Tokens, ggf. Secret) speichert der Hub verschlüsselt mit Windows **DPAPI** (`ProtectedData`, CurrentUser) unter `%APPDATA%\VoicedeckEdge\`. Nie im Widget, nie im Repo, nie im Log.

| Funktion | RPC-Befehle / Events |
|---|---|
| Voice-Kanal live | `GET_SELECTED_VOICE_CHANNEL`, Events `VOICE_STATE_CREATE/UPDATE/DELETE`, `SPEAKING_START/STOP`, `VOICE_CHANNEL_SELECT` |
| Lautstärke / lokal stumm pro Person | `SET_USER_VOICE_SETTINGS` (`volume` 0–200, `mute`) |
| Mute / Deafen / VA ↔ PTT | `GET_VOICE_SETTINGS`, `SET_VOICE_SETTINGS` (`mute`, `deaf`, `mode.type`), Event `VOICE_SETTINGS_UPDATE` |
| Verlassen | `SELECT_VOICE_CHANNEL` mit `channel_id: null` |
| Server & Kanäle | `GET_GUILDS` (id, name, icon_url), `GET_CHANNELS` (nur id, name, type), `GET_CHANNEL` (zusätzlich `position`, `voice_states`, `messages`), `SELECT_VOICE_CHANNEL` |
| Live-Chat lesen | `GET_CHANNEL` (liefert letzte Nachrichten), Events `MESSAGE_CREATE/UPDATE/DELETE` |
| Verbindungsstatus | Event `VOICE_CONNECTION_STATUS` |

### 1.2 Tray-App (Hub)

- Tray-Icon mit Status-Farbe: grün = verbunden, gelb = Discord nicht offen, rot = nicht eingerichtet/Fehler. Tooltip sagt in einem Satz, was zu tun ist.
- Menü: *Status*, *Einrichtungs-Assistent…*, *Gekoppelte Widgets…* (Kopplung aufheben), *Mit Windows starten* (HKCU\…\Run, standardmäßig an), *Nach Updates suchen* (opt-in), *Abmelden und alle Daten löschen*, *Log öffnen*, *Beenden*.
- Verbindet sich automatisch neu, wenn Discord neu startet (Backoff 2 s → 30 s).
- Sprache automatisch nach Windows (Deutsch/Englisch).
- **Auslieferung als Installer** (Inno Setup, `VoicedeckEdge-Setup.exe`) mit **self-contained** .NET 8 – der Nutzer muss keine Runtime installieren. Installation ohne Admin-Rechte nach `%LOCALAPPDATA%\Programs\`. Größer (ca. 60–70 MB), dafür kein Zusatzschritt. Deinstallation über Windows „Apps“ entfernt auch alle Daten (Rückfrage).

### 1.3 Sicherheit der lokalen Schnittstelle

Jede Webseite im Browser könnte `ws://localhost:47820` ansprechen. Darum:

1. Bindung nur an `127.0.0.1`.
2. **Kopplung per Klick statt Code:** Verbindet sich ein neues Widget, zeigt der Hub ein Fenster im Vordergrund: „Ein Widget möchte Discord steuern – *Erlauben* / *Ablehnen*“. Nach *Erlauben* bekommt das Widget ein zufälliges Geräte-Token (256 Bit), das es in seinem `localStorage` (Schlüssel mit `uniqueId`) speichert. Danach verbindet es sich still. Kein Abtippen, und eine fremde Webseite kommt nicht unbemerkt durch.
3. **Herkunfts-Sperre** (`OriginPolicy`): iCUE lädt Widgets als lokale Datei und sendet `Origin: null` (geprüft 2026-10-27, iCUE 5.52). Erlaubt sind nur `null` und Verbindungen ohne Origin (lokale Programme). Jede echte Webseite (`http(s)://…`) und Browser-Erweiterungen werden schon vor dem *Erlauben?*-Fenster mit HTTP 403 abgewiesen. Für Browser-Tests: Hub mit `--dev` starten (lässt `http://localhost` / `http://127.0.0.1` zu). Abgewiesene Herkünfte werden nur einmal pro Hub-Start geloggt.
4. Der Hub reicht nur die oben gelisteten Befehle durch – keine freie RPC-Weiterleitung.
5. Gekoppelte Widgets sind im Tray-Menü sichtbar und einzeln widerrufbar.
6. Bricht ein Widget die Frage ab (z. B. Neuladen), zählt das **nicht** als Ablehnung – nur echtes *Ablehnen* löst die 5-Sekunden-Sperre aus.

**Abwägung Geräte-Token im `localStorage`:** Die Skill-Checkliste rät von Zugangsdaten im `localStorage` ab. iCUE bietet Widgets aber keinen anderen dauerhaften Speicher; ohne Token müsste man bei jedem Widget-Start neu „Erlauben“ klicken. Das Token gilt nur für den Hub auf diesem PC (127.0.0.1), ist einzeln widerrufbar und wird im Hub nur als Hash gespeichert. Ein fremdes Widget, das es lesen könnte, bräuchte zusätzlich die Berechtigung `localhost:47820` – die zeigt iCUE beim Installieren an. Bewusst so entschieden (2026-10-27).

### 1.4 Protokoll Widget ↔ Hub (Kurzform)

```jsonc
// Widget → Hub
{ "op": "hello", "token": "<Geräte-Token oder leer>", "proto": 1 }   // immer zuerst
{ "op": "pair.request" }                                             // nach pair_denied erneut fragen
{ "op": "voice.toggleMute", "id": 1 } | { "op": "voice.toggleDeaf", "id": 2 } | { "op": "voice.setMode", "mode": "PUSH_TO_TALK", "id": 3 }
{ "op": "voice.leave", "id": 4 } | { "op": "voice.join", "channelId": "…", "id": 5 }
{ "op": "user.setVolume", "userId": "…", "volume": 140, "id": 6 } | { "op": "user.setMute", "userId": "…", "mute": true } | { "op": "user.reset", "userId": "…" }
{ "op": "guilds.list" } | { "op": "channels.list", "guildId": "…" } | { "op": "chat.open", "channelId": "…" } | { "op": "chat.close" }
{ "op": "audio.setInput", "deviceId": "…" } | { "op": "audio.setOutput", "deviceId": "…" }      // nur Geräte aus Discords aktueller Liste
{ "op": "audio.setInputVolume", "volume": 80 } | { "op": "audio.setOutputVolume", "volume": 150 } // Eingang 0–100, Ausgang 0–200
{ "op": "edge.page", "dir": "next|prev" }                                                       // drückt das iCUE-Kürzel (Wischen)

// Hub → Widget
{ "t": "pair_pending" } → { "t": "paired", "token": "…" } | { "t": "pair_denied" }
{ "t": "welcome", "proto": 1, "hubVersion": "0.1.0", "imageBase": "http://localhost:47820", "imageKey": "…" }
{ "t": "snapshot", "data": { "status", "me", "voice", "settings", "chat", "dms", "unread" } }   // vollständiger Zustand nach welcome
// settings.input/output = { deviceId, volume, devices: [{ id, name }] } · unread = { guilds: {id: n}, channels: {id: n} }
{ "t": "state", "section": "voice", "data": { … } }                            // ganze Sektion bei jeder Änderung
{ "t": "speaking", "userId": "…", "speaking": true }                           // häufig, darum einzeln
{ "t": "result", "id": 6, "ok": true, "data": … } | { "t": "result", "id": 6, "ok": false, "error": "invalid_args" }
{ "t": "error", "code": "proto_mismatch|not_paired|bad_json" }
```

Bilder: `imageBase + avatar/icon-Pfad + "?k=" + imageKey` (Schlüssel neu bei jedem Hub-Start, ohne Schlüssel HTTP 403). `discord`-Status: `not_configured`, `invalid_client_id`, `discord_not_running`, `connecting`, `unauthorized`, `authorizing`, `ready`.

Protokoll-Version `proto` erlaubt spätere Erweiterungen; Widget zeigt Hinweis, wenn Hub zu alt ist.

### 1.5 Einrichtung für Anwender

Ziel: in ca. 5 Minuten fertig, ohne technische Vorkenntnisse. Der Nutzer tippt nichts außer einmal der Client-ID (per Kopieren/Einfügen).

1. **`VoicedeckEdge-Setup.exe` von GitHub laden und starten.** Installiert ohne Admin, startet den Hub und öffnet den Assistenten.
2. **Assistent, Seite „Discord-App anlegen“:** Knopf *Developer Portal öffnen* (öffnet die richtige Seite im Browser), daneben bebilderte Mini-Anleitung in 3 Schritten (App benennen → Redirect `http://localhost` eintragen, Wert mit Kopier-Knopf → Client-ID kopieren). Der Assistent prüft die eingefügte Client-ID sofort auf Format.
3. **Seite „Mit Discord verbinden“:** Knopf *Verbinden* → Discord zeigt seinen Zustimmungsdialog → *Autorisieren*. Der Assistent zeigt einen grünen Haken. Ist Discord nicht offen, sagt er das und wartet automatisch.
4. **Seite „Widget installieren“:** Knopf *Widget zu iCUE hinzufügen* öffnet die mitgelieferte `.icuewidget`-Datei mit iCUE. Kurzer Hinweis, wo man es auf der Xeneon Edge platziert.
5. **Widget auf der Edge platzieren** → Hub fragt einmal *Erlauben?* → fertig.

Weitere Vereinfachungen:
- Widget erkennt Port und Hub automatisch; **keine Pflichtfelder** in den iCUE-Einstellungen.
- Widget-Sprache folgt `iCUE.iCUELanguage` (Deutsch/Englisch).
- Jede Fehlermeldung im Widget nennt **genau einen nächsten Schritt** in Alltagssprache, keine Fehlercodes.
- `docs/SETUP.md` mit Screenshots, plus kurze Fehlerhilfe („Widget zeigt …“ → „Mach …“).

---

## 2. Widget

- **Gerät:** nur `dashboard_lcd` (Xeneon Edge), `"interactive": true` (Touch).
- **Größen:** Medium H (840×696), Large H (1688×696), XL H (2536×696). Kleinere/vertikale Slots werden nicht beworben; das Widget zeigt dort notfalls nur die Voice-Ansicht.
- **min_app_version:** `5.52` (wegen `permissions`).

### 2.1 Layout je Größe

Bereiche: **A** Voice-Kanal (Teilnehmer), **B** Steuerleiste, **C** Server & Kanäle, **D** Chat.

```
Medium 840×696 – Tabs                Large 1688×696 – 2 Spalten
┌──────────────────────────┐          ┌──────────────────────┬──────────────────────┐
│ [Voice] [Kanäle] [Chat]  │          │ A Voice-Kanal        │ [Kanäle] [Chat]      │
│                          │          │  (Kacheln)           │                      │
│  aktiver Tab (A/C/D)     │          │                      │  C oder D            │
│                          │          │                      │                      │
├──────────────────────────┤          ├──────────────────────┤                      │
│ B  🎤  🎧  PTT/VA   ⎋    │          │ B  🎤  🎧  PTT/VA  ⎋ │                      │
└──────────────────────────┘          └──────────────────────┴──────────────────────┘

XL 2536×696 – 3 Spalten
┌───────────────┬──────────────────────┬──────────────────────┐
│ C Server &    │ A Voice-Kanal        │ D Chat               │
│   Kanäle      │  (große Kacheln)     │                      │
│               ├──────────────────────┤                      │
│               │ B  🎤  🎧  PTT/VA  ⎋ │                      │
└───────────────┴──────────────────────┴──────────────────────┘
```

Umschaltung per CSS-`aspect-ratio`-Media-Queries (M ≈ 1.2, L ≈ 2.4, XL ≈ 3.6), Größen über eine `--layout-unit`-Basis.

### 2.2 Bereiche im Detail

- **A Voice-Kanal:** Kanalname + Server oben. Teilnehmer als Kacheln (Avatar, Name, grüner Sprech-Ring, Icons für stumm/taub/lokal stumm). Darstellung *Kacheln* oder *Liste* (Einstellung). Tippen auf Person → **Popup**: Lautstärke-Slider 0–200 % (Discord-Standard 100 %), *Lokal stummschalten*, *Zurücksetzen*.
  Leerzustand: „Du bist in keinem Sprachkanal“ + Knopf *Kanäle öffnen*.
- **B Steuerleiste:** Mute, Deafen, Modus VA/PTT, Verlassen (mit optionaler 2-Tipp-Bestätigung). Aktiver Zustand farbig (rot für stumm/taub), immer aus Discords echtem Zustand.
- **C Server & Kanäle:** Server-Leiste (Icons), darunter Kanalliste mit Symbolen für Voice/Text. Tipp auf Voice → beitreten, Tipp auf Text → Chat öffnen. **Favoriten**: langes Drücken pinnt einen Kanal; Favoriten erscheinen oben als Chips. Gespeichert per `localStorage` mit `uniqueId`.
- **D Chat:** Letzte ~50 Nachrichten (Avatar, Name, Uhrzeit, Text; Anhänge als „📎 Datei“), live. Nur lesen. Standard-Chat optional per Einstellung (Kanal-ID).

### 2.3 Zustände

| Zustand | Anzeige |
|---|---|
| Laden | Skeleton-Kacheln + „Verbinde mit Voicedeck Edge Hub…“ |
| Hub nicht gefunden | „Voicedeck Edge Hub läuft nicht. Starte ihn über das Startmenü.“ (Erstinstallation: Link-Text zur Download-Seite), Auto-Retry alle 5 s |
| Wartet auf Erlaubnis | „Bestätige am PC das Fenster *Widget erlauben*.“ |
| Kopplung abgelehnt/widerrufen | „Zugriff wurde abgelehnt. Tippe hier, um erneut zu fragen.“ |
| Discord nicht offen | „Discord ist nicht geöffnet“, Auto-Retry (Hub meldet, sobald Discord da ist) |
| Nicht eingerichtet | „Einrichtung noch nicht fertig: Klicke am PC auf das Voicedeck-Symbol in der Taskleiste.“ |
| Verbindung verloren | Letzter Stand bleibt sichtbar, ausgegraut + Offline-Badge, Auto-Retry |
| Inhalt | normale Ansicht |
| Vorschau (`iCUE.isPreview`) | Demo-Daten (fiktive Teilnehmer), damit die iCUE-Vorschau etwas zeigt |

### 2.4 iCUE-Einstellungen

**Gruppe „Voicedeck Edge“**

| Property | Typ | Standard | Beschreibung |
|---|---|---|---|
| `theme` | tab-buttons | `auto` | Automatisch / Dunkel / Hell |
| `voiceLayout` | tab-buttons | `tiles` | Kacheln / Liste |
| `confirmLeave` | switch | `true` | Verlassen erst nach zweitem Tipp |
| `showChat` | switch | `true` | Chat-Bereich anzeigen |
| `privateChat` | switch | `false` | Streamer-/Privatmodus: Chat-Texte unscharf, bis man draufgetippt |
| `textScale` | slider | `100` (80–140 %) | Textgröße (Knopftexte der Steuerleiste nur bis 115 %, feste Knopfbreite) |

**Gruppe „Widget-Personalisierung“** (feste Reihenfolge laut Skill)

| Property | Typ | Standard |
|---|---|---|
| `textColor` | color | `#ffffff` (dunkel) |
| `accentColor` | color | `#5865F2` |
| `backgroundColor` | color | `#1e1f22` |
| `backgroundMedia` | media-selector | leer |
| `bgBrightness` | slider | 70 % (20–100) |
| `glassBlur` | slider | 0 |
| `transparency` | slider | 0 |

Hinweis (geprüft 2026-10-27 auf der Edge): iCUE blendet `textColor`, `accentColor`, `backgroundColor`, `backgroundMedia` und `transparency` nur bei eingeschaltetem **„Benutzerdefinierter Stil“** ein; sonst übergibt es die Farben und die Transparenz der Seiten-Personalisierung und **kein** eigenes Bild (das Seitenbild liegt hinter dem Widget). Darum: `theme` = **Automatisch** (Standard, nimmt iCUEs Farben) / Dunkel / Hell (erzwingen die Voicedeck-Palette für Text und Hintergrund, Akzent bleibt von iCUE). `bgBrightness` und `glassBlur` wirken nur auf ein eigenes Bild und heißen deshalb „… (nur eigenes Bild)“. `pathToAsset` kommt URL-kodiert. Hintergrundbild: eigene kleine Umsetzung (`scripts/media-background.js`) statt Corsairs MediaViewer-Code (Lizenz); bei aktivem Bild legt sich ein Schleier in Hintergrundfarbe darüber (Lesbarkeit).

### 2.5 manifest.json (geplant)

```json
{
  "name": "Voicedeck Edge",
  "interactive": true,
  "os": [{ "platform": "windows" }],
  "supported_devices": [{ "type": "dashboard_lcd" }],
  "min_app_version": "5.52",
  "permissions": [
    { "type": "url", "domain": "localhost", "port": 47820 }
  ]
}
```

Keine Plugins nötig. Der Port ist fest und nicht einstellbar, damit die deklarierte Berechtigung immer passt und iCUE nie nachfragen muss. Ist der Port belegt, meldet der Hub das verständlich im Tray (sehr seltener Fall).

### 2.6 Berechtigungen

| Teil | Zugriff | Grund |
|---|---|---|
| Widget | `url` → `localhost:47820` | WebSocket + Bilder vom Hub |
| Hub | Discord Named Pipe | RPC mit lokalem Discord-Client |
| Hub | HTTPS `discord.com` | OAuth-Token holen/erneuern |
| Hub | HTTPS `cdn.discordapp.com` | Avatare/Icons laden (Cache nur im Arbeitsspeicher) |
| Hub | HTTPS `api.github.com` | Nur wenn Update-Suche eingeschaltet (opt-in) |

---

### 2.7 Erweiterungen nach v0.9-Test (2026-10-27)

- **Chat-Bilder und Server-Emojis:** Der Hub registriert jedes Bild einer Nachricht (Anhänge, Bild-Embeds) in `MediaRegistry` und gibt dem Widget nur einen Token-Pfad `/img/m/{token}`. Nur `cdn.discordapp.com`, `media.discordapp.net` und `images-ext-*.discordapp.net` (HTTPS), Vorschau max. 480 px Breite, max. 4 Bilder pro Nachricht, nur im Speicher. Server-Emojis über `/img/emojis/[a/]{id}`, auch für Reaktionen (`reactions`: Emoji, Anzahl, `me`; max. 20 pro Nachricht, live über `MESSAGE_UPDATE`). Keine freie URL-Weiterleitung.
- **Privatmodus-Knopf** im Chat-Kopf: überschreibt die iCUE-Einstellung im Widget (`localStorage`); ändert sich die iCUE-Einstellung, gilt wieder sie. Verdeckt auch Bilder und Emojis.
- **Mikro-Interaktionen** nach interior.dev (MIT, Copyright (c) 2026 ozzy), in reinem CSS/JS nachgebaut (`scripts/motion.js`, `styles/motion.css`): Press Depth (alle Knöpfe), Hold to Confirm (Verlassen, 1,2 s – ersetzt „zweimal tippen“), Long Press (anheften), Segmented Control (Tabs), Slider Detents (Lautstärke 50/100/150/200), New Items Pill (Chat), Blur-up Image (Chat-Bilder). `prefers-reduced-motion` schaltet Bewegung ab. Kanalliste und Chat werden nur bei echten Inhaltsänderungen neu aufgebaut (sonst brechen Gesten ab).
- **Direktnachrichten** (Spike + Edge-Test 2026-10-27): RPC hat keine DM-Liste (`GET_CHANNELS` ohne Server liefert alle Serverkanäle, keine DMs). DMs werden über `NOTIFICATION_CREATE` (Scope `rpc.notifications.read`) erkannt; `GET_CHANNEL` (bis ~50 Nachrichten) und `SUBSCRIBE MESSAGE_CREATE` funktionieren für DM-Kanäle (Typ 1/3). Liste der letzten 20 Unterhaltungen **nur im Arbeitsspeicher** (bewusste Datenschutz-Entscheidung), Zähler für Ungelesenes, Anzeige wie in Discord (DM-Symbol oben in der Server-Spalte). Ältere Anmeldungen ohne den Scope → Hub verlangt einmal neue Autorisierung.
- **Netzwerk:** Hub antwortet nur Loopback-Adressen (http.sys lauscht auf allen Schnittstellen, Host-Header ist fälschbar).

## 3. Datenschutz

Grundsatz: **Voicedeck Edge betreibt keine eigenen Server und sammelt keine Daten.** Alles läuft auf dem PC des Nutzers; Verbindungen gehen nur zu Discord selbst (und optional zu GitHub für Updates).

| Daten | Wo | Wie lange | Hinweis |
|---|---|---|---|
| Discord-Tokens, ggf. Secret | `%APPDATA%\VoicedeckEdge\`, DPAPI-verschlüsselt | bis „Abmelden“ / Deinstallation | nur für das eigene Windows-Konto lesbar |
| Client-ID, Einstellungen | `%APPDATA%\VoicedeckEdge\settings.json` | bis Deinstallation | keine personenbezogenen Daten |
| Gekoppelte Widgets | Hub: nur Hash des Geräte-Tokens + Kopplungsdatum | bis Widerruf | |
| Chat-Nachrichten, Teilnehmer, Server/Kanäle | **nur Arbeitsspeicher** (Hub + Widget) | solange angezeigt | nie auf Festplatte, nie im Log |
| Avatare, Server-Icons, Server-Emojis, Chat-Bilder | **nur Arbeitsspeicher** des Hubs, max. 50 MB | bis Hub-Neustart (Chat-Bild-Zuordnung bis Kanalwechsel) | |
| Widget-`localStorage` | iCUE | bis Widget entfernt | Geräte-Token, Favoriten (Kanal-ID, -Name, Server-ID), zuletzt gewählter Server und Chat-Kanal – keine Nachrichten. Im Demo-/Vorschau-Modus wird nichts gespeichert. |
| Log-Datei | `%LOCALAPPDATA%\VoicedeckEdge\logs\` | rotierend, max. 7 Tage / 5 MB | nur technische Ereignisse; Tokens, Nachrichten, Namen und IDs werden nicht geloggt |

Maßnahmen:
- **Keine Telemetrie, keine Analyse, keine Absturzberichte** an Dritte. Kein Tracking im Widget, keine externen Schriften/CDNs (alles lokal im Paket).
- **Datensparsam:** nur die Scopes, die für die Funktionen nötig sind; Chat lädt nur den gerade geöffneten Kanal.
- **Update-Suche ist opt-in:** Der Assistent fragt einmal mit nicht vorausgewähltem Kästchen. Sie sendet nur eine anonyme Anfrage an die GitHub-Releases-API.
- **„Abmelden und alle Daten löschen“** im Tray (für den Deinstaller auch als `VoicedeckEdgeHub.exe --delete-all-data`, ohne Oberfläche): widerruft das Token bei Discord (`/oauth2/token/revoke`), löscht alle Dateien in `%APPDATA%\VoicedeckEdge\` und die Logs, hebt alle Kopplungen auf.
- **Privatmodus:** Text wird transparent über grauen Balken, Avatare verschwinden, dazu leichte Unschärfe. Bewusst nicht nur ein Blur-Filter – der ließ im Test Wörter noch erahnen (2026-10-27).
- **Daten anderer Personen:** Namen, Avatare und Nachrichten von Mitspielern werden nur angezeigt, nie gespeichert. Für Streamer gibt es den Privatmodus (`privateChat`) und den Schalter `showChat`.
- **Transparenz:** `PRIVACY.md` im Repo erklärt das in einfacher Sprache (Deutsch + Englisch), verlinkt im README und im Assistenten.
- Kein Code von Drittanbietern zur Laufzeit nachgeladen; Abhängigkeiten im Hub so wenige wie möglich, mit festen Versionen.

---

## 4. Repo-Struktur

```
voicedeck-edge/
├─ README.md                  Überblick, Screenshots, Schnellstart
├─ LICENSE                    MIT (Vorschlag)
├─ PRIVACY.md                 Datenschutz in einfacher Sprache (DE/EN)
├─ docs/
│  ├─ DESIGN.md               dieses Dokument
│  └─ SETUP.md                Schritt-für-Schritt mit Screenshots + Fehlerhilfe
├─ hub/                       C# .NET 8 Tray-App
│  ├─ VoicedeckEdge.Hub.csproj
│  ├─ Discord/                RPC-Client (Pipe, Frames, OAuth/PKCE)
│  ├─ Server/                 WebSocket + /img (HttpListener), Kopplung
│  ├─ Tray/                   Tray-Icon, Menü
│  └─ Setup/                  Einrichtungs-Assistent
├─ hub.Tests/                 xUnit-Tests (Protokoll, Zustandsmodell, Log-Filter)
├─ installer/                 Inno-Setup-Skript (enthält Hub + .icuewidget)
└─ widget/                    iCUE-Widget-Paket
   ├─ index.html
   ├─ manifest.json
   ├─ translation.json        Deutsch + Englisch
   ├─ styles/VoicedeckEdge.css
   ├─ scripts/               app, hub-client, views
   └─ resources/              Icons (eigene SVGs)
```

---

## 5. Umsetzungs-Reihenfolge

1. **Spike Hub ↔ Discord:** RPC-Verbindung, Autorisierung (Public Client + PKCE?), minimale Scopes, `GET_SELECTED_VOICE_CHANNEL` – prüft die offenen Punkte unten.
2. Hub: WebSocket-Server, Kopplung per Klick, Zustandsmodell, Bild-Cache im Speicher, Tray-Menü, Log-Filter.
3. Hub: Einrichtungs-Assistent, „Abmelden und alle Daten löschen“.
4. Widget: Grundgerüst, Verbindung, Zustände, Voice-Bereich + Steuerleiste (Medium).
5. Widget: Server & Kanäle, Favoriten, Chat, Privatmodus; Layouts Large und XL.
6. Hell/Dunkel, Einstellungen, Übersetzungen, Demo-Modus für Vorschau.
7. Installer, Tests, `icuewidget validate` + `package`, README/SETUP/PRIVACY, GitHub-Release.

## 6. Offene Punkte / Risiken

- ~~**Nur Client-ID (PKCE)**~~ – **gelöst:** funktioniert (Spike 2026-10-27).
- **Chat-Verlauf (geklärt):** `GET_CHANNEL` liefert **bis zu 30** letzte Nachrichten (gemessen: 0–30 je Kanal). Danach live über `MESSAGE_CREATE`, im Speicher max. 50.
- **Kanaltypen:** Server enthalten auch Forum- (15) und Ankündigungskanäle (5). Ankündigungen werden wie Text behandelt, Foren in v1 ausgeblendet.
- **Eigene Discord-App pro Nutzer bleibt nötig** – das ist die größte Hürde und lässt sich wegen Discords Regeln nicht vermeiden, nur gut anleiten.
- **Seitenwechsel auf der Edge (bestätigt 2026-10-27):** Über *interaktiven* Widgets kann man in iCUE nicht zwischen Seiten wischen – iCUE gibt jede Berührung an das Widget. Gilt auch für Corsairs eigene Widgets (Gegentest mit Sensorliste/Kalender), es gibt keine Widget-API für den Seitenwechsel. Widget gibt waagerechte Gesten trotzdem frei (`touch-action: pan-y`, Liste `overflow-x: hidden`) für den Fall, dass iCUE das später unterstützt. **SETUP.md/README:** Hinweis „zum Seitenwechsel auf freier Fläche oder über nicht-interaktivem Widget wischen“.
- ~~**iCUE-Dateizuordnung**~~ – **gelöst (2026-10-27):** iCUE ist für `.icuewidget` registriert; der Assistent-Knopf öffnet iCUE und importiert das Widget direkt.

- **Kanal-Reihenfolge und Kategorien (Spike-Ergebnis):** `GET_CHANNELS` liefert nur id, name, type. `GET_CHANNEL` liefert `position`, aber **kein `parent_id`** – Kategorien (Typ 4) sind zwar in der Liste, aber Kanäle lassen sich ihnen nicht zuordnen. Lösung v1: Hub fragt beim Öffnen eines Servers `GET_CHANNEL` je Kanal ab (gecacht im Speicher), Liste zeigt **Sprachkanäle und Textkanäle als zwei Gruppen, jeweils nach `position` sortiert**. Keine Kategorie-Überschriften.
- **Gute Nachricht für Voice:** `voice_states` enthält pro Person bereits `volume`, lokales `mute`, `nick`, Avatar und Mute/Deaf-Status – alles, was Bereich A braucht.
- **Kamera / Bildschirm teilen:** Bewusst nicht in v1 – RPC bietet dafür keine zuverlässige Steuerung.
- **Soundboard:** Nicht in v1 (nicht gewünscht).
- **Discord-Änderungen:** RPC ist offiziell „private beta“ und kann sich ändern. Hub kapselt alles, Widget bleibt unberührt.
- **Push to Talk:** Umschalten des Modus geht; die PTT-Taste selbst kann das Widget nicht drücken.
