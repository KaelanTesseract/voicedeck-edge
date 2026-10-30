// Body texts (tr() only works in iCUE metadata, not in the page itself).
// Language follows iCUE.iCUELanguage, falling back to the browser language.
var VoicedeckI18n = (function () {
  var texts = {
    en: {
      tabVoice: 'Voice', tabChannels: 'Channels', tabChat: 'Chat',
      comingSoon: 'Coming in the next version.',
      offline: 'Offline',
      noChannel: 'You are not in a voice channel',
      openChannels: 'Open channels',
      mute: 'Mute', unmute: 'Unmute', deafen: 'Deafen', undeafen: 'Undeafen',
      voiceActivity: 'Voice activity', pushToTalk: 'Push to talk',
      leave: 'Leave',
      you: 'You',
      audio: 'Audio', windowsDefault: 'Windows default', audioTitle: 'Audio devices', audioInput: 'Input', audioOutput: 'Output',
      inputVolume: 'Input volume', outputVolume: 'Output volume', noDevices: 'Discord reports no devices.',
      volume: 'Volume', localMute: 'Mute for me', localUnmute: 'Unmute for me', reset: 'Reset',
      connectingHub: 'Connecting to Voicedeck Edge Hub …',
      hubMissingTitle: 'Voicedeck Edge Hub is not running',
      hubMissingHint: 'Start “Voicedeck Edge Hub” from the Start menu on this PC. The widget connects automatically.',
      pairPendingTitle: 'Confirm on your PC',
      pairPendingHint: 'Click “Allow” in the window “Allow widget?” on your PC.',
      pairDeniedTitle: 'Access was denied',
      pairDeniedHint: 'This widget may not control Discord yet.',
      askAgain: 'Ask again',
      protoTitle: 'Please update Voicedeck Edge',
      protoHint: 'Widget and hub versions do not match. Install the latest version of both.',
      setupTitle: 'Setup is not finished',
      setupHint: 'Click the Voicedeck icon in the taskbar on your PC and follow the steps.',
      discordMissingTitle: 'Discord is not running',
      discordMissingHint: 'Start Discord on your PC. The widget continues automatically.',
      connectingDiscord: 'Connecting to Discord …',
      actionFailed: 'That did not work – please try again.',
      voiceChannels: 'Voice channels', textChannels: 'Text channels',
      pinHint: 'Press and hold a channel to pin it here.',
      pinned: 'Pinned', unpinned: 'Unpinned', orderSaved: 'Server order saved',
      loading: 'Loading …', noGuilds: 'No servers found.', noChannels: 'No channels you can use here.',
      chatEmpty: 'Pick a text channel', chatPick: 'Open channels', noMessages: 'No messages yet.',
      attachment: 'attachment', attachments: 'attachments', embed: 'embedded content', edited: '(edited)',
      tapToReveal: 'Private mode – tap to show messages',
      members: 'in channel',
      holdToLeave: 'Press and hold to leave',
      privacyOn: 'Private', privacyOff: 'Visible',
      newMessage: 'new message', newMessages: 'new messages',
      volumeDefault: 'default',
      directMessages: 'Direct messages',
      dmEmpty: 'Direct messages show up here as soon as someone writes to you while the hub is running. They are not stored.'
    },
    de: {
      tabVoice: 'Sprache', tabChannels: 'Kanäle', tabChat: 'Chat',
      comingSoon: 'Kommt in der nächsten Version.',
      offline: 'Offline',
      noChannel: 'Du bist in keinem Sprachkanal',
      openChannels: 'Kanäle öffnen',
      mute: 'Stumm', unmute: 'Stumm aus', deafen: 'Taub', undeafen: 'Taub aus',
      voiceActivity: 'Sprach­aktivierung', pushToTalk: 'Push to Talk', // soft hyphen: breaks on narrow buttons
      leave: 'Verlassen',
      you: 'Du',
      audio: 'Audio', windowsDefault: 'Windows-Standard', audioTitle: 'Audiogeräte', audioInput: 'Eingabe', audioOutput: 'Ausgabe',
      inputVolume: 'Eingangslautstärke', outputVolume: 'Ausgangslautstärke', noDevices: 'Discord meldet keine Geräte.',
      volume: 'Lautstärke', localMute: 'Für mich stumm', localUnmute: 'Für mich laut', reset: 'Zurücksetzen',
      connectingHub: 'Verbinde mit Voicedeck Edge Hub …',
      hubMissingTitle: 'Voicedeck Edge Hub läuft nicht',
      hubMissingHint: 'Starte „Voicedeck Edge Hub“ über das Startmenü an diesem PC. Das Widget verbindet sich dann von selbst.',
      pairPendingTitle: 'Bitte am PC bestätigen',
      pairPendingHint: 'Klicke am PC im Fenster „Widget erlauben?“ auf „Erlauben“.',
      pairDeniedTitle: 'Zugriff wurde abgelehnt',
      pairDeniedHint: 'Dieses Widget darf Discord noch nicht steuern.',
      askAgain: 'Erneut fragen',
      protoTitle: 'Bitte Voicedeck Edge aktualisieren',
      protoHint: 'Widget und Hub passen nicht zusammen. Installiere von beiden die neueste Version.',
      setupTitle: 'Einrichtung noch nicht fertig',
      setupHint: 'Klicke am PC auf das Voicedeck-Symbol in der Taskleiste und folge den Schritten.',
      discordMissingTitle: 'Discord ist nicht geöffnet',
      discordMissingHint: 'Starte Discord an deinem PC. Das Widget macht dann von selbst weiter.',
      connectingDiscord: 'Verbinde mit Discord …',
      actionFailed: 'Das hat nicht geklappt – bitte noch einmal.',
      voiceChannels: 'Sprachkanäle', textChannels: 'Textkanäle',
      pinHint: 'Halte einen Kanal gedrückt, um ihn hier anzuheften.',
      pinned: 'Angeheftet', unpinned: 'Gelöst', orderSaved: 'Server-Reihenfolge gespeichert',
      loading: 'Lade …', noGuilds: 'Keine Server gefunden.', noChannels: 'Hier gibt es keine Kanäle, die du nutzen kannst.',
      chatEmpty: 'Wähle einen Textkanal', chatPick: 'Kanäle öffnen', noMessages: 'Noch keine Nachrichten.',
      attachment: 'Anhang', attachments: 'Anhänge', embed: 'eingebetteter Inhalt', edited: '(bearbeitet)',
      tapToReveal: 'Privatmodus – tippen, um Nachrichten zu zeigen',
      members: 'im Kanal',
      holdToLeave: 'Zum Verlassen gedrückt halten',
      privacyOn: 'Privat', privacyOff: 'Sichtbar',
      newMessage: 'neue Nachricht', newMessages: 'neue Nachrichten',
      volumeDefault: 'Standard',
      directMessages: 'Direktnachrichten',
      dmEmpty: 'Direktnachrichten erscheinen hier, sobald dir jemand schreibt, während der Hub läuft. Sie werden nicht gespeichert.'
    }
  };

  var lang = 'en';

  function setLanguage(code) {
    var short = String(code || '').slice(0, 2).toLowerCase();
    lang = texts[short] ? short : 'en';
  }

  function t(key) {
    return (texts[lang] && texts[lang][key]) || texts.en[key] || key;
  }

  setLanguage(typeof navigator !== 'undefined' ? navigator.language : 'en');
  function locale() { return lang === 'de' ? 'de-DE' : 'en-US'; }

  return { t: t, setLanguage: setLanguage, locale: locale };
})();
