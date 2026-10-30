# Einrichtung / Setup – Voicedeck Edge

[Deutsch](#deutsch) · [English](#english)

---

## Deutsch

Dauer: etwa 5 Minuten, nur einmal nötig.

**Du brauchst:** Windows 10/11 (64 Bit), CORSAIR iCUE **5.52 oder neuer**, eine **XENEON EDGE** und die **Discord-Desktop-App** (nicht die Browser-Version).

### Schritt 1 – Installieren

1. Lade `VoicedeckEdge-Setup-….exe` von der [Releases-Seite](https://github.com/KaelanTesseract/voicedeck-edge/releases) herunter.
2. Starte die Datei. Admin-Rechte sind nicht nötig.
   - Zeigt Windows **„Der Computer wurde durch Windows geschützt“**, klicke auf **Weitere Informationen** → **Trotzdem ausführen**. Das erscheint, weil der Installer nicht digital signiert ist (Signaturen kosten Geld). Die Prüfsummen jeder Version stehen in `SHA256SUMS.txt` auf der Releases-Seite.
3. Lass „Voicedeck Edge Hub mit Windows starten“ angehakt und klicke auf **Installieren**, danach auf **Fertigstellen**.

Der **Einrichtungs-Assistent** öffnet sich von selbst. Unten rechts in der Taskleiste erscheint ein lila **„V“**.

### Schritt 2 – Eigene Discord-App anlegen

Discord erlaubt die Steuerung nur über eine *eigene* App. Das klingt technisch, sind aber nur ein paar Klicks:

1. Klicke im Assistenten auf **Developer Portal öffnen** und melde dich mit Discord an.
2. Klicke auf **New Application**, gib einen Namen ein (z. B. `Voicedeck Edge`, Kopieren-Knopf im Assistenten), akzeptiere die Bedingungen und klicke **Create**.
3. Klicke links auf **OAuth2**:
   - unter **Redirects** auf **Add Redirect** klicken und `http://localhost` eintragen (Kopieren-Knopf im Assistenten),
   - **Public Client** einschalten,
   - unten **Save Changes** klicken.
4. Kopiere oben auf derselben Seite die **Client ID** und füge sie im Assistenten ein. Der Assistent zeigt „✓ Sieht gut aus“.

> ⚠️ Kopiere **nicht** das *Client Secret* – Voicedeck Edge braucht es nicht.

### Schritt 3 – Mit Discord verbinden

1. Klicke im Assistenten auf **Weiter**.
2. In Discord erscheint ein Fenster – klicke dort auf **Autorisieren**.
3. Der Assistent zeigt „✓ Verbunden mit Discord“.

### Schritt 4 – Widget zu iCUE hinzufügen

1. Klicke im Assistenten auf **Widget zu iCUE hinzufügen** – iCUE öffnet sich und importiert das Widget.
   - Falls nicht (z. B. ältere iCUE-Version): Öffne in iCUE deine **XENEON EDGE** → Bereich **Widgets** → **„+“** über der Liste → wähle
     `%LOCALAPPDATA%\Programs\VoicedeckEdge\VoicedeckEdge.icuewidget`.
2. Ziehe **Voicedeck Edge** auf die Edge, am besten in der Größe **M, L oder XL (waagerecht)**.
3. Am PC erscheint einmal **„Widget erlauben?“** → **Erlauben**. Fertig!

### Tipps

- **Größen:** M zeigt Tabs (Sprache · Kanäle · Chat), L zwei Spalten, XL alles nebeneinander.
- **Kanal anheften:** Kanal **lange drücken**, bis er sich ganz gefüllt hat → er erscheint oben als Favorit. Nochmal lange drücken löst ihn.
- **Audiogeräte:** Knopf **Audio** in der Steuerleiste. Unter *Eingabe* wählst du das Mikrofon, unter *Ausgabe* Kopfhörer oder Lautsprecher, jeweils mit Lautstärkeregler – wie in Discords Spracheinstellungen.
- **Server sortieren:** Server-Symbol **gedrückt halten**, bis es sich anhebt, dann an die gewünschte Stelle ziehen. Die Reihenfolge wird nur im Widget gespeichert (Discord gibt deine eigene Sortierung nicht an andere Programme weiter). Neue Server erscheinen unten.
- **Rote Zahlen:** zeigen neue Nachrichten, für die dich Discord benachrichtigen würde – an Server und Kanal. Öffnest du den Kanal im Widget, verschwindet die Zahl. Nur solange der Hub läuft, nichts wird gespeichert.
- **Kanal verlassen:** „Verlassen“ **gedrückt halten**, bis die rote Füllung durch ist (lässt sich in den Widget-Einstellungen auf einfaches Tippen umstellen).
- **Direktnachrichten:** Sprechblasen-Symbol ganz oben in der Server-Spalte. Unterhaltungen erscheinen, sobald dir jemand schreibt, während der Hub läuft (Discord meldet DMs nur über Benachrichtigungen; die Liste wird nicht gespeichert). Nach einem Update fragt der Hub einmal nach der neuen Berechtigung „Benachrichtigungen lesen“ – einfach auf das Tray-Symbol klicken und in Discord autorisieren.
- **Lautstärke einer Person:** auf die Person tippen → Regler 0–200 %, „Für mich stumm“, „Zurücksetzen“.
- **Privatmodus:** Knopf **„Sichtbar/Privat“ oben im Chat** oder in den Widget-Einstellungen. Chat, Bilder, Emojis und Reaktionen bleiben unkenntlich, bis du darauftippst – gut zum Streamen.
- **Hintergrundbild, eigene Farben, Transparenz:** in den Widget-Einstellungen **„Benutzerdefinierter Stil“** einschalten. Ohne ihn übernimmt das Widget die Farben deiner iCUE-Seite. „Design“ kann trotzdem **Dunkel** oder **Hell** erzwingen.
- **Seiten wechseln auf der Edge (Wischen):** iCUE gibt Wischgesten über Touch-Widgets nicht weiter. Voicedeck Edge erkennt das Wischen selbst und drückt für dich das iCUE-Tastenkürzel zum Umblättern. Einmal einrichten:
  1. In iCUE auf deine **XENEON EDGE** klicken und **Geräte-Einstellungen** öffnen.
  2. Unter *Bildschirmeinrichtung* in **Vorheriger Anzeigebildschirm** klicken und **Strg + Alt + Umschalt + Pfeil links** drücken.
  3. In **Nächster Anzeigebildschirm** klicken und **Strg + Alt + Umschalt + Pfeil rechts** drücken.
  4. Fertig: Auf dem Widget nach links wischen = nächste Seite, nach rechts = vorherige. Ein kleiner Pfeil bestätigt das Wischen.

  Abschalten lässt es sich in den Widget-Einstellungen („Wischen wechselt die Seite“). Der Hub drückt nur genau diese beiden Kürzel und nur, wenn du auf dem Widget wischst.

### Wenn etwas nicht klappt

| Das Widget zeigt … | Das hilft |
|---|---|
| „Voicedeck Edge Hub läuft nicht“ | Startmenü → **Voicedeck Edge Hub** starten. |
| „Bitte am PC bestätigen“ | Am PC im Fenster **„Widget erlauben?“** auf **Erlauben** klicken. |
| „Zugriff wurde abgelehnt“ | Auf der Edge **Erneut fragen** tippen und am PC erlauben. |
| „Discord ist nicht geöffnet“ | Discord-Desktop-App starten – das Widget macht von selbst weiter. |
| „Einrichtung noch nicht fertig“ | Am PC auf das lila **„V“** in der Taskleiste klicken und den Assistenten abschließen. |
| „Offline“ (ausgegraut) | Der Hub wurde beendet oder neu gestartet – das Widget verbindet sich automatisch neu. |
| Assistent: „Discord kennt diese Client-ID nicht“ | Client-ID im Developer Portal unter **OAuth2** nochmal kopieren (nicht das Secret). |
| Assistent: Autorisierung klappt nicht | Prüfen, ob `http://localhost` als Redirect eingetragen und **Public Client** an ist, dann **Erneut versuchen**. |

Weitere Hilfe: Tray-Menü → **Log öffnen** und ein [Issue](https://github.com/KaelanTesseract/voicedeck-edge/issues) erstellen (das Log enthält keine Namen oder Nachrichten).

### Entfernen

Windows-Einstellungen → **Apps** → **Voicedeck Edge** → Deinstallieren. Du wirst gefragt, ob auch alle Daten gelöscht werden sollen (inkl. Abmeldung bei Discord). Das Widget entfernst du in iCUE.

---

## English

Takes about 5 minutes, only once.

**You need:** Windows 10/11 (64-bit), CORSAIR iCUE **5.52 or newer**, a **XENEON EDGE** and the **Discord desktop app** (not the browser version).

### Step 1 – Install

1. Download `VoicedeckEdge-Setup-….exe` from the [releases page](https://github.com/KaelanTesseract/voicedeck-edge/releases).
2. Run it. No admin rights needed.
   - If Windows shows **"Windows protected your PC"**, click **More info** → **Run anyway**. This appears because the installer is not code-signed (signing costs money). Checksums for every version are in `SHA256SUMS.txt` on the releases page.
3. Keep "Start Voicedeck Edge Hub with Windows" checked, click **Install**, then **Finish**.

The **setup assistant** opens by itself, and a purple **"V"** appears in the taskbar notification area.

### Step 2 – Create your own Discord app

Discord only allows control through *your own* app. It sounds technical but is just a few clicks:

1. In the assistant click **Open Developer Portal** and sign in with Discord.
2. Click **New Application**, enter a name (e.g. `Voicedeck Edge`, copy button in the assistant), accept the terms and click **Create**.
3. Click **OAuth2** on the left:
   - under **Redirects** click **Add Redirect** and enter `http://localhost` (copy button in the assistant),
   - switch on **Public Client**,
   - click **Save Changes** at the bottom.
4. Copy the **Client ID** at the top of that page and paste it into the assistant. It shows "✓ Looks good".

> ⚠️ Do **not** copy the *Client Secret* – Voicedeck Edge does not need it.

### Step 3 – Connect to Discord

1. Click **Next** in the assistant.
2. Discord shows a window – click **Authorize**.
3. The assistant shows "✓ Connected to Discord".

### Step 4 – Add the widget to iCUE

1. Click **Add widget to iCUE** in the assistant – iCUE opens and imports the widget.
   - If it does not (e.g. an older iCUE version): in iCUE open your **XENEON EDGE** → **Widgets** → **"+"** above the list → choose
     `%LOCALAPPDATA%\Programs\VoicedeckEdge\VoicedeckEdge.icuewidget`.
2. Place **Voicedeck Edge** on the Edge, ideally size **M, L or XL (horizontal)**.
3. Your PC asks once **"Allow widget?"** → **Allow**. Done!

### Tips

- **Sizes:** M uses tabs (Voice · Channels · Chat), L two columns, XL everything side by side.
- **Pin a channel:** **press and hold** it until it is filled → it appears at the top as a favourite. Hold again to unpin.
- **Audio devices:** **Audio** button in the control bar. *Input* picks the microphone, *Output* the headphones or speakers, each with a volume slider – like Discord's voice settings.
- **Arrange servers:** **press and hold** a server icon until it lifts, then drag it where you want it. The order is saved in the widget only (Discord doesn't share your own sorting with other programs). New servers appear at the bottom.
- **Red numbers:** new messages Discord would notify you about – on the server and the channel. Opening the channel in the widget clears the number. Only while the hub runs, nothing is stored.
- **Leave a channel:** **press and hold** "Leave" until the red fill completes (can be switched to a single tap in the widget settings).
- **Direct messages:** speech-bubble icon at the top of the server column. Conversations appear as soon as someone writes to you while the hub runs (Discord only reports DMs through notifications; the list is not stored). After an update the hub asks once for the new permission "read notifications" – click the tray icon and authorize in Discord.
- **Someone's volume:** tap the person → slider 0–200 %, "Mute for me", "Reset".
- **Private mode:** **"Visible/Private" button at the top of the chat** or in the widget settings. Chat, images, emojis and reactions stay unreadable until tapped – good for streaming.
- **Background image, own colours, transparency:** switch on **"Custom Style"** in the widget settings. Without it, the widget uses your iCUE page colours; "Theme" can still force **Dark** or **Light**.
- **Switching pages on the Edge (swipe):** iCUE does not pass swipes over touch widgets through. Voicedeck Edge detects the swipe itself and presses iCUE's page shortcut for you. Set it up once:
  1. In iCUE, click your **XENEON EDGE** and open **Device settings**.
  2. Under *Screen setup*, click **Previous display screen** and press **Ctrl + Alt + Shift + Left arrow**.
  3. Click **Next display screen** and press **Ctrl + Alt + Shift + Right arrow**.
  4. Done: swipe left on the widget for the next page, right for the previous one. A small arrow confirms the swipe.

  You can turn this off in the widget settings ("Swipe to change pages"). The hub only ever presses these two shortcuts, and only when you swipe on the widget.

### Troubleshooting

| The widget shows … | Do this |
|---|---|
| "Voicedeck Edge Hub is not running" | Start **Voicedeck Edge Hub** from the Start menu. |
| "Confirm on your PC" | Click **Allow** in the **"Allow widget?"** window on your PC. |
| "Access was denied" | Tap **Ask again** on the Edge and allow it on your PC. |
| "Discord is not running" | Start the Discord desktop app – the widget continues by itself. |
| "Setup is not finished" | Click the purple **"V"** in the taskbar and finish the assistant. |
| "Offline" (greyed out) | The hub stopped or restarted – the widget reconnects automatically. |
| Assistant: "Discord does not know this client ID" | Copy the Client ID again under **OAuth2** (not the secret). |
| Assistant: authorization fails | Check that `http://localhost` is a redirect and **Public Client** is on, then **Try again**. |

More help: tray menu → **Open log** and open an [issue](https://github.com/KaelanTesseract/voicedeck-edge/issues) (the log contains no names or messages).

### Uninstall

Windows Settings → **Apps** → **Voicedeck Edge** → Uninstall. You are asked whether to delete all data as well (including signing out of Discord). Remove the widget in iCUE.
