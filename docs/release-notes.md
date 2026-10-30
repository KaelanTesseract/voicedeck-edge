## Voicedeck Edge 0.9.7 (Beta)

- **Never miss new messages further down:** when a server or channel with new messages is scrolled out of view, a red pill at the top or bottom edge of the list shows how many – tap it to jump there.
- **Fixed: large servers didn't open** ("That didn't work"). The hub now returns the channel list after 10 seconds at the latest, even if Discord is still loading some channels; those are sorted by name until the next time. A single channel Discord can't answer for no longer breaks the whole list, and channels Discord never answers for are skipped from then on, so the server opens quickly the next time.

**Download:** `VoicedeckEdge-Setup-0.9.7.exe` – install over the previous version; the setup assistant then opens on the widget step so you can re-import the widget with one click.

---

## Voicedeck Edge 0.9.5 (Beta)

- **Reactions in the chat:** emoji reactions appear under messages like in Discord – with count, server emojis as pictures, and highlighted when one of them is yours. Hidden in private mode.

**Download:** `VoicedeckEdge-Setup-0.9.5.exe` – install over the previous version; the setup assistant then opens on the widget step so you can re-import the widget with one click.

---

## Voicedeck Edge 0.9.4 (Beta)

- **Audio devices:** new *Audio* button in the control bar. Choose your microphone and your speakers/headphones for Discord and set the input and output volume – right on the Edge.
- **Smoother server sorting:** the server now follows your finger, the others slide aside, and you can move it any distance in one go (the column scrolls along at the edges). In 0.9.2 a server only moved one position at a time on the XENEON EDGE.
- **Updating is easier:** after installing a new version, the setup assistant opens by itself on the widget step, so you can re-import the widget with one click.
- Fixed: the update-check option in the assistant was cut off.

**Download:** `VoicedeckEdge-Setup-0.9.4.exe` – install over the previous version; the setup assistant then opens on the widget step so you can re-import the widget with one click.

---

## Voicedeck Edge 0.9.2 (Beta)

- **Arrange your servers:** press and hold a server icon, then drag it – set it up once like your Discord sidebar (Discord doesn't share your own order with other programs).
- **Red unread numbers on servers and text channels** for messages Discord notifies you about, like in Discord. Opening the channel in the widget clears them. Memory only, nothing is stored.

**Download:** `VoicedeckEdge-Setup-0.9.2.exe` – install over the previous version, your sign-in and pairing are kept. Re-import the widget in iCUE (button in the setup assistant).

---

## Voicedeck Edge 0.9.1 (Beta)

**New: swipe to change pages.** iCUE doesn't pass swipes over touch widgets through, so you couldn't leave a page that Voicedeck Edge sits on. Now the widget recognises a left/right swipe and the hub presses iCUE's page shortcut for you. One-time setup in iCUE (XENEON EDGE → device settings): *Previous display screen* = **Ctrl + Alt + Shift + Left arrow**, *Next display screen* = **Ctrl + Alt + Shift + Right arrow**. Details: [docs/SETUP.md](https://github.com/KaelanTesseract/voicedeck-edge/blob/main/docs/SETUP.md#english). Can be switched off in the widget settings.

**Download:** `VoicedeckEdge-Setup-0.9.1.exe` – install over 0.9.0, your sign-in and pairing are kept. Re-import the widget in iCUE (button in the setup assistant or double-click `VoicedeckEdge.icuewidget`).

---

## Voicedeck Edge 0.9.0 (Beta)

Control Discord from your CORSAIR XENEON EDGE. First public beta.

**Download:** `VoicedeckEdge-Setup-0.9.0.exe` – installs the hub and opens the setup assistant. Guide: [docs/SETUP.md](https://github.com/KaelanTesseract/voicedeck-edge/blob/main/docs/SETUP.md)

**Requirements:** Windows 10/11 x64 · iCUE 5.52+ · XENEON EDGE · Discord desktop app

### Included
- Voice channel with speaking indicator, mute/deafen badges, per-person volume (0–200 %)
- Mute, deafen, voice activity / push to talk, leave
- Servers & channels, favourites (press and hold), live chat with images and server emojis, private mode
- Direct messages with unread badge
- Layouts for M, L and XL; design automatic/dark/light; text size; background image
- Setup assistant, "sign out and delete all data", optional update check
- No servers, no tracking – see [PRIVACY.md](https://github.com/KaelanTesseract/voicedeck-edge/blob/main/PRIVACY.md)

### Notes
- Direct messages appear once someone writes to you while the hub runs (Discord only reports DMs through notifications).
- iCUE does not allow swiping between Edge pages over touch widgets.

`SHA256SUMS.txt` lists the checksums of the downloads.

---

**Deutsch:** Erste öffentliche Beta. Installer laden, dem Assistenten folgen – Anleitung in [docs/SETUP.md](https://github.com/KaelanTesseract/voicedeck-edge/blob/main/docs/SETUP.md). Rückmeldungen sind willkommen.
