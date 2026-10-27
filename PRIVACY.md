# Datenschutz / Privacy – Voicedeck Edge

[Deutsch](#deutsch) · [English](#english)

---

## Deutsch

**Kurz gesagt:** Voicedeck Edge hat keine eigenen Server und sammelt keine Daten. Alles läuft auf deinem PC.

### Mit wem spricht Voicedeck Edge?

| Ziel | Wozu | Wann |
|---|---|---|
| Discord-App auf deinem PC | Sprachkanal, Kanäle, Chat anzeigen und steuern | immer, solange der Hub läuft |
| `discord.com` | Anmeldung (einmalig) und Erneuerung der Anmeldung | beim Einrichten, danach selten |
| `cdn.discordapp.com` | Profilbilder und Server-Icons laden | wenn sie angezeigt werden |
| `api.github.com` | Prüfen, ob es eine neue Version gibt | **nur** wenn du „Nach Updates suchen“ einschaltest |

Sonst nichts: keine Telemetrie, keine Analyse, keine Absturzberichte, keine Werbung, kein Tracking.

### Was wird gespeichert?

| Was | Wo | Wie lange |
|---|---|---|
| Deine Discord-Anmeldung (Tokens) | `%APPDATA%\VoicedeckEdge\tokens.bin`, mit Windows verschlüsselt (nur für dein Windows-Konto lesbar) | bis „Abmelden und alle Daten löschen“ oder Deinstallation |
| Client-ID deiner Discord-App, Einstellungen | `%APPDATA%\VoicedeckEdge\settings.json` | bis Löschen/Deinstallation |
| Gekoppelte Widgets | `%APPDATA%\VoicedeckEdge\pairings.json` – nur ein Prüfwert (Hash), nie der Schlüssel selbst | bis du die Kopplung aufhebst |
| Technisches Protokoll | `%LOCALAPPDATA%\VoicedeckEdge\logs\` – ohne Namen, IDs, Nachrichten oder Tokens | höchstens 7 Tage |
| Im Widget (iCUE) | Kopplungsschlüssel, angeheftete Kanäle (ID, Name), zuletzt gewählter Server und Chat | bis du das Widget entfernst |

**Nie gespeichert** werden Chat-Nachrichten, Direktnachrichten, Bilder, Namen und Profilbilder anderer Personen oder Teilnehmerlisten. Sie liegen nur im Arbeitsspeicher, solange sie angezeigt werden.

**Direktnachrichten:** Damit private Unterhaltungen im Widget erscheinen können, liest der Hub deine Discord-**Benachrichtigungen** (Berechtigung „Benachrichtigungen lesen“). Er beachtet davon nur Direktnachrichten und merkt sich die Unterhaltungen ausschließlich im Arbeitsspeicher – nach einem Neustart von Hub oder Discord ist die Liste leer.

### Deine Kontrolle

- **Alles löschen:** Rechtsklick auf das Voicedeck-Symbol in der Taskleiste → „Abmelden und alle Daten löschen“. Das widerruft die Anmeldung bei Discord und löscht alle oben genannten Dateien. Bei der Deinstallation wirst du dasselbe gefragt.
- **Kopplungen aufheben:** Tray-Menü → „Gekoppelte Widgets …“.
- **Privatmodus** im Widget: Chat-Texte werden unkenntlich, bis du darauftippst – praktisch beim Streamen.
- **Chat ausblenden:** Widget-Einstellung „Chat anzeigen“.
- Discord selbst zeigt die Anmeldung unter *Einstellungen → Autorisierte Apps*; dort kannst du sie jederzeit entfernen.

### Daten anderer Personen

Voicedeck Edge zeigt nur, was du in Discord ohnehin sehen kannst, und speichert davon nichts. Denke beim Streamen daran, dass Namen und Nachrichten anderer auf deinem Bildschirm sichtbar sein können (dafür gibt es den Privatmodus).

---

## English

**In short:** Voicedeck Edge has no servers of its own and collects no data. Everything runs on your PC.

### Who does Voicedeck Edge talk to?

| Destination | Purpose | When |
|---|---|---|
| Discord app on your PC | show and control voice, channels and chat | while the hub is running |
| `discord.com` | sign-in (once) and renewing it | during setup, rarely afterwards |
| `cdn.discordapp.com` | load avatars and server icons | when they are shown |
| `api.github.com` | check whether a new version exists | **only** if you turn on "Check for updates" |

Nothing else: no telemetry, no analytics, no crash reports, no ads, no tracking.

### What is stored?

| What | Where | How long |
|---|---|---|
| Your Discord sign-in (tokens) | `%APPDATA%\VoicedeckEdge\tokens.bin`, encrypted by Windows (readable only by your Windows account) | until "Sign out and delete all data" or uninstall |
| Client ID of your Discord app, settings | `%APPDATA%\VoicedeckEdge\settings.json` | until deleted/uninstalled |
| Paired widgets | `%APPDATA%\VoicedeckEdge\pairings.json` – only a hash, never the key itself | until you unpair |
| Technical log | `%LOCALAPPDATA%\VoicedeckEdge\logs\` – no names, IDs, messages or tokens | at most 7 days |
| Inside the widget (iCUE) | pairing key, pinned channels (ID, name), last server and chat | until you remove the widget |

**Never stored:** chat messages, direct messages, images, other people's names and avatars, member lists. They exist only in memory while shown.

**Direct messages:** so that private conversations can appear in the widget, the hub reads your Discord **notifications** (permission "read notifications"). It only looks at direct messages and keeps the conversation list in memory only – after restarting the hub or Discord the list is empty.

### You are in control

- **Delete everything:** right-click the Voicedeck icon in the taskbar → "Sign out and delete all data". This revokes the sign-in at Discord and deletes all files listed above. The uninstaller asks the same.
- **Unpair widgets:** tray menu → "Paired widgets …".
- **Private mode** in the widget hides chat text until you tap it – handy when streaming.
- **Hide chat:** widget setting "Show chat".
- Discord lists the sign-in under *Settings → Authorized Apps*, where you can remove it at any time.

### Other people's data

Voicedeck Edge only shows what you can already see in Discord and stores none of it. When streaming, remember that other people's names and messages may be visible on your screen (that is what private mode is for).
