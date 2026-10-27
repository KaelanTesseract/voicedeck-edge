// Voicedeck Edge widget – rendering and interaction. All Discord logic lives in the hub;
// this file only shows the hub's state and sends the user's taps back as commands.

// iCUE API binding – bare assignment is intentional (see lifecycle reference).
icueEvents = {
  onDataUpdated: onIcueDataUpdated,
  onICUEInitialized: onIcueInitialized
};

var t = VoicedeckI18n.t;
var $ = function (id) { return document.getElementById(id); };

var app = {
  client: null,
  started: false,
  demo: false,
  connection: 'idle',
  hadDiscord: false,      // true once Discord was ready – keeps content visible (greyed) when offline
  hub: null,              // welcome message: imageBase, imageKey
  data: null,             // { status, me, voice, settings, chat }
  tab: 'voice',           // medium: visible view; large: right pane (channels/chat)
  layout: 'm',            // 'm' | 'l' | 'xl' – from the slot's aspect ratio
  lastDiscord: null,
  popupUserId: null,
  popupDragging: false
};

// ---------- iCUE properties ----------

function getIcueProperty(name) {
  if (typeof window !== 'undefined' && Object.prototype.hasOwnProperty.call(window, name)) {
    var value = window[name];
    if (value !== undefined && value !== null && value !== '') return value;
  }
  try {
    var v = Function('return typeof ' + name + ' !== "undefined" ? ' + name + ' : undefined')();
    if (v !== undefined && v !== null && v !== '') return v;
  } catch (e) {}
  return undefined;
}

function clampRange(v, min, max, d) {
  v = Number(v);
  return Number.isFinite(v) ? Math.max(min, Math.min(max, v)) : d;
}

function colorProperty(name, fallback) {
  var v = getIcueProperty(name);
  return typeof v === 'string' && v ? v : fallback;
}

// Design: "auto" uses the colours iCUE injects – the page personalisation, or the widget's own
// custom style (verified 2026-10-27: with custom style off iCUE sends the page colours, e.g. #ffffff on
// #000000). "dark"/"light" force the Voicedeck palette for text and background; the accent stays.
var PALETTES = {
  dark: { text: '#f2f3f5', background: '#1e1f22' },
  light: { text: '#1f2329', background: '#f2f3f5' }
};

var mediaBackground = null;

function applyStyles() {
  var root = document.documentElement.style;
  var theme = getIcueProperty('theme');
  var forced = PALETTES[theme] || null;
  document.documentElement.dataset.theme = theme === 'light' || theme === 'dark' ? theme : 'auto';
  root.setProperty('--text-color', forced ? forced.text : colorProperty('textColor', PALETTES.dark.text));
  root.setProperty('--accent-color', colorProperty('accentColor', '#5865f2'));
  root.setProperty('--bg-color', forced ? forced.background : colorProperty('backgroundColor', PALETTES.dark.background));
  root.setProperty('--bg-opacity', String(1 - clampRange(getIcueProperty('transparency'), 0, 100, 0) / 100));
  root.setProperty('--bg-blur', clampRange(getIcueProperty('glassBlur'), 0, 30, 0) + 'px');
  root.setProperty('--bg-brightness', clampRange(getIcueProperty('bgBrightness'), 20, 100, 70) + '%');
  root.setProperty('--ts', String(clampRange(getIcueProperty('textScale'), 80, 140, 100) / 100));
  $('members').classList.toggle('is-list', getIcueProperty('voiceLayout') === 'list');

  if (!mediaBackground) mediaBackground = new VoicedeckMedia.MediaBackground($('media-background'));
  mediaBackground.show(getIcueProperty('backgroundMedia'));
}

function confirmLeaveEnabled() {
  return getIcueProperty('confirmLeave') !== false;
}

// ---------- per-widget storage (device token, favourites, last server/chat) ----------

// Demo/preview data must never end up next to real data, so demo mode keeps its store in memory.
var demoStore = {};

function storageKey() {
  var id = getIcueProperty('uniqueId');
  return typeof id === 'string' && id ? id : 'voicedeck-edge-browser';
}

function loadStore() {
  if (app.demo) return JSON.parse(JSON.stringify(demoStore));
  try {
    var raw = localStorage.getItem(storageKey());
    var parsed = raw ? JSON.parse(raw) : {};
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch (e) {
    return {};
  }
}

function saveStore(patch) {
  if (app.demo) {
    for (var d in patch) demoStore[d] = patch[d];
    return;
  }
  try {
    var store = loadStore();
    for (var k in patch) store[k] = patch[k];
    localStorage.setItem(storageKey(), JSON.stringify(store));
  } catch (e) { /* storage unavailable – pairing will be asked again next time */ }
}

// ---------- lifecycle ----------

function onIcueDataUpdated() {
  applyStyles();
  render();
}

function onIcueInitialized() {
  if (typeof iCUE !== 'undefined' && iCUE.iCUELanguage) VoicedeckI18n.setLanguage(iCUE.iCUELanguage);
  var preview = typeof iCUE !== 'undefined' && iCUE.isPreview === true;
  start(preview);
}

/** Preview/demo shows sample data and never contacts the hub (no pairing prompts from previews). */
function start(demo) {
  if (app.started) { onIcueDataUpdated(); return; }
  app.started = true;
  app.demo = demo;
  applyTexts();
  applyStyles();

  if (demo) {
    app.connection = 'ready';
    app.hadDiscord = true;
    app.hub = null;
    app.data = VoicedeckDemo.snapshot();
    applyDemoOptions();
    render();
    return;
  }

  app.client = new VoicedeckHubClient({
    loadToken: function () { return loadStore().token; },
    saveToken: function (token) { saveStore({ token: token }); },
    onState: function (state) {
      app.connection = state;
      if (state === 'pair_denied' || state === 'pair_pending') saveStore({ token: null });
      render();
    },
    onMessage: onHubMessage
  });
  app.client.start();
  render();
}

/**
 * Browser demo only (?demo&tab=chat&theme=light&private=1&guild=dms&popup=Robin&lang=en):
 * sets up a specific view for documentation screenshots. Never active with real data.
 */
function applyDemoOptions() {
  var q = new URLSearchParams(location.search);
  if (q.get('lang')) VoicedeckI18n.setLanguage(q.get('lang'));
  if (q.get('theme')) window.theme = q.get('theme');
  if (q.get('private') === '1') window.privateChat = true;
  if (q.get('layout') === 'list') window.voiceLayout = 'list';
  if (q.get('tab')) app.tab = q.get('tab');
  applyTexts();
  applyStyles();
  if (q.get('guild') === 'dms') saveStore({ guildId: '@dms' });
  var popup = q.get('popup');
  if (popup) {
    setTimeout(function () {
      var m = app.data.voice.members.filter(function (x) { return x.name === popup; })[0];
      if (m) openPopup(m.id);
    }, 50);
  }
}

function onHubMessage(msg) {
  switch (msg.t) {
    case 'welcome':
      app.hub = { imageBase: msg.imageBase, imageKey: msg.imageKey };
      resetChannels();
      Chat.autoOpened = false;
      return;
    case 'snapshot':
      app.data = msg.data;
      break;
    case 'state':
      if (!app.data) return;
      app.data[msg.section] = msg.data;
      break;
    case 'speaking':
      setSpeaking(msg.userId, msg.speaking);
      return;
    default:
      return;
  }
  var discord = app.data && app.data.status ? app.data.status.discord : null;
  if (discord !== app.lastDiscord) {
    // Discord (re)connected or went away: server/channel lists may be outdated.
    app.lastDiscord = discord;
    resetChannels();
    Chat.autoOpened = false;
  }
  if (discord === 'ready') app.hadDiscord = true;
  render();
}

function isDiscordReady() {
  return !!(app.data && app.data.status && app.data.status.discord === 'ready');
}

// ---------- rendering ----------

function applyTexts() {
  document.querySelector('[data-tab="voice"]').textContent = t('tabVoice');
  document.querySelector('[data-tab="channels"]').textContent = t('tabChannels');
  document.querySelector('[data-tab="chat"]').textContent = t('tabChat');
  $('offline-badge').textContent = t('offline');
  $('voice-empty-text').textContent = t('noChannel');
  $('open-channels').textContent = t('openChannels');
  $('popup-volume-label').textContent = t('volume');
  $('popup-reset').textContent = t('reset');
  $('chat-empty-text').textContent = t('chatEmpty');
  $('chat-pick').textContent = t('chatPick');
}

function render() {
  if (!app.started) return;
  renderOverlay();
  renderLayout();
  if (!app.data) return;
  renderVoice();
  renderControls();
  syncPopup();
  renderChannels();
  renderChat();
  ensureChannelsLoaded();
  autoOpenLastChat();
}

/** Decides which full-screen hint (if any) covers the widget. */
function overlayFor() {
  var c = app.connection;
  var discord = app.data && app.data.status ? app.data.status.discord : null;
  if (app.demo) return null;
  if (c === 'idle' || c === 'connecting') return { spinner: true, title: t('connectingHub') };
  if (c === 'unreachable') return { title: t('hubMissingTitle'), hint: t('hubMissingHint') };
  if (c === 'pair_pending') return { spinner: true, title: t('pairPendingTitle'), hint: t('pairPendingHint') };
  if (c === 'pair_denied') return { title: t('pairDeniedTitle'), hint: t('pairDeniedHint'), action: t('askAgain') };
  if (c === 'proto_mismatch') return { title: t('protoTitle'), hint: t('protoHint') };
  if (c === 'lost') return app.hadDiscord ? null : { title: t('hubMissingTitle'), hint: t('hubMissingHint') };
  switch (discord) {
    case 'ready': return null;
    case 'discord_not_running': return { title: t('discordMissingTitle'), hint: t('discordMissingHint') };
    case 'connecting':
    case 'authorizing': return { spinner: true, title: t('connectingDiscord') };
    case 'not_configured':
    case 'invalid_client_id':
    case 'unauthorized': return { title: t('setupTitle'), hint: t('setupHint') };
    default: return { spinner: true, title: t('connectingHub') };
  }
}

function renderOverlay() {
  var o = overlayFor();
  var stale = !app.demo && app.connection === 'lost' && app.hadDiscord;
  $('app').classList.toggle('is-stale', stale);
  $('offline-badge').hidden = !stale;
  $('state-overlay').hidden = !o;
  if (!o) return;
  $('state-spinner').hidden = !o.spinner;
  $('state-title').textContent = o.title;
  $('state-hint').textContent = o.hint || '';
  $('state-hint').hidden = !o.hint;
  $('state-action').hidden = !o.action;
  if (o.action) $('state-action').textContent = o.action;
  if (o) closePopup();
}

/** Same breakpoints as the CSS: XL ≈ 3.6:1, L ≈ 2.4:1, M ≈ 1.2:1 (horizontal XENEON EDGE slots). */
function computeLayout() {
  if (window.matchMedia('(min-aspect-ratio: 3/1)').matches) return 'xl';
  if (window.matchMedia('(min-aspect-ratio: 17/10)').matches) return 'l';
  return 'm';
}

/**
 * Medium: one view at a time behind tabs. Large: voice always, channels or chat on the right.
 * XL: everything side by side, no tabs.
 */
function renderLayout() {
  var layout = app.layout = computeLayout();
  var chatOn = chatEnabled();
  var root = $('app');
  root.dataset.layout = layout;
  root.classList.toggle('no-chat', !chatOn);
  if (!chatOn && app.tab === 'chat') app.tab = layout === 'm' ? 'voice' : 'channels';

  var pane = layout === 'l' && app.tab === 'voice' ? 'channels' : app.tab;
  var shown = {
    voice: layout !== 'm' || app.tab === 'voice',
    channels: layout === 'xl' || pane === 'channels',
    chat: chatOn && (layout === 'xl' || pane === 'chat')
  };
  ['voice', 'channels', 'chat'].forEach(function (v) { $('view-' + v).classList.toggle('is-shown', shown[v]); });
  // A chat rendered while hidden could not scroll (no layout yet): catch up when it becomes visible.
  if (shown.chat && !app.chatWasShown) keepPinnedToBottom();
  app.chatWasShown = shown.chat;

  var tabs = document.querySelectorAll('.tab');
  for (var i = 0; i < tabs.length; i++) {
    var name = tabs[i].dataset.tab;
    tabs[i].hidden = (name === 'voice' && layout === 'l') || (name === 'chat' && !chatOn);
    tabs[i].classList.toggle('is-active', name === pane);
  }
  // Unread direct messages: a dot on the channels tab so they are noticed from the voice view too.
  var channelsTab = document.querySelector('.tab[data-tab="channels"]');
  channelsTab.classList.toggle('has-badge', layout !== 'xl' && pane !== 'channels' && unreadDirect() > 0);
  document.querySelector('.topbar').hidden = layout === 'xl' || (layout === 'l' && !chatOn);
  VoicedeckMotion.placeThumb(document.querySelector('.tabs'));
}

function imageUrl(path) {
  if (!path) return null;
  if (app.demo && path.indexOf('/img/') !== 0) return path; // demo data uses files from the package
  if (!app.hub || !app.hub.imageBase) return null;
  return app.hub.imageBase + path + '?k=' + encodeURIComponent(app.hub.imageKey);
}

/** Avatar circle: hub image, or initials on a colour derived from the user id. */
function fillAvatar(el, person) {
  var url = imageUrl(person.avatar);
  el.textContent = '';
  el.style.backgroundImage = '';
  if (url) {
    el.style.backgroundImage = 'url("' + url + '")';
    el.style.backgroundColor = 'transparent';
  } else {
    // FNV-1a: IDs differ mostly in their last digits, a plain sum would give everyone the same colour.
    var hash = 2166136261;
    for (var i = 0; i < person.id.length; i++) hash = Math.imul(hash ^ person.id.charCodeAt(i), 16777619);
    hash = Math.imul(hash ^ (hash >>> 16), 0x85ebca6b); // final mix so the last digit reaches every bit
    hash = Math.imul(hash ^ (hash >>> 13), 0xc2b2ae35);
    hash = (hash ^ (hash >>> 16)) >>> 0;
    el.style.backgroundColor = 'hsl(' + (hash % 360) + ', 45%, 42%)';
    el.textContent = (person.name || '?').trim().charAt(0).toUpperCase();
  }
}

function icon(name, extraClass) {
  var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'icon' + (extraClass ? ' ' + extraClass : ''));
  svg.setAttribute('aria-hidden', 'true');
  var use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', '#' + name);
  svg.appendChild(use);
  return svg;
}

function sortedMembers() {
  var meId = app.data.me ? app.data.me.id : null;
  return app.data.voice.members.slice().sort(function (a, b) {
    if (a.id === meId) return -1;
    if (b.id === meId) return 1;
    return a.name.localeCompare(b.name);
  });
}

function renderVoice() {
  var voice = app.data.voice;
  var channel = voice && voice.channel;
  document.querySelector('.voice-header').hidden = !channel;
  $('members').hidden = !channel;
  $('voice-empty').hidden = !!channel;
  if (!channel) return;

  $('voice-channel').textContent = channel.name;
  $('voice-guild').textContent = channel.guildName || '';
  var dot = $('connection-dot');
  dot.className = 'connection-dot ' + (voice.connection === 'VOICE_CONNECTED' ? 'is-good'
    : voice.connection === 'DISCONNECTED' ? 'is-bad' : 'is-busy');

  var meId = app.data.me ? app.data.me.id : null;
  var list = $('members');
  var keepScroll = list.scrollTop; // the list is rebuilt on every update – keep the user's position
  list.textContent = '';
  sortedMembers().forEach(function (m) {
    var tile = document.createElement('button');
    tile.className = 'member' + (m.speaking ? ' is-speaking' : '') + (m.id === meId ? ' is-self' : '');
    tile.dataset.id = m.id;

    var avatar = document.createElement('div');
    avatar.className = 'avatar';
    fillAvatar(avatar, m);
    tile.appendChild(avatar);

    var name = document.createElement('span');
    name.className = 'member-name';
    name.textContent = m.name;
    tile.appendChild(name);

    var flags = document.createElement('span');
    flags.className = 'member-flags';
    if (m.selfMute || m.serverMute) flags.appendChild(icon('i-mic-off'));
    if (m.selfDeaf || m.serverDeaf) flags.appendChild(icon('i-headphones-off'));
    if (m.localMute) flags.appendChild(icon('i-mic-off', 'is-local'));
    tile.appendChild(flags);

    if (m.id !== meId && m.volume !== 100) {
      var vol = document.createElement('span');
      vol.className = 'member-volume';
      vol.textContent = m.volume + '%';
      tile.appendChild(vol);
    }

    if (m.id !== meId) tile.addEventListener('click', function () { openPopup(m.id); });
    list.appendChild(tile);
  });
  list.scrollTop = keepScroll;
}

function setSpeaking(userId, speaking) {
  if (!app.data || !app.data.voice) return;
  app.data.voice.members.forEach(function (m) { if (m.id === userId) m.speaking = speaking; });
  var tile = $('members').querySelector('[data-id="' + CSS.escape(userId) + '"]');
  if (tile) tile.classList.toggle('is-speaking', speaking);
}

function setControl(id, opts) {
  var btn = $(id);
  btn.querySelector('use').setAttribute('href', '#' + opts.icon);
  btn.querySelector('span').textContent = opts.label;
  btn.classList.toggle('is-long', opts.label.length > 11);
  btn.classList.toggle('is-on', !!opts.on);
  btn.classList.toggle('is-accent', !!opts.accent);
  btn.disabled = !!opts.disabled;
}

function renderControls() {
  var s = app.data.settings || {};
  var inChannel = !!(app.data.voice && app.data.voice.channel);
  var ptt = s.mode === 'PUSH_TO_TALK';
  setControl('btn-mute', { icon: s.mute ? 'i-mic-off' : 'i-mic', label: s.mute ? t('unmute') : t('mute'), on: s.mute });
  setControl('btn-deaf', { icon: s.deaf ? 'i-headphones-off' : 'i-headphones', label: s.deaf ? t('undeafen') : t('deafen'), on: s.deaf });
  setControl('btn-mode', { icon: ptt ? 'i-ptt' : 'i-wave', label: ptt ? t('pushToTalk') : t('voiceActivity'), accent: ptt });

  // Leave: label exists twice (base + the copy inside the hold fill), so the button never changes width.
  var leave = $('btn-leave');
  var labels = leave.querySelectorAll('.leave-label');
  for (var i = 0; i < labels.length; i++) labels[i].textContent = t('leave');
  leave.disabled = !inChannel;
  leave.classList.toggle('is-hold', confirmLeaveEnabled());
}

function doLeave() {
  command('voice.leave', null, function () { app.data.voice.channel = null; app.data.voice.members = []; }).catch(function () {});
}

// ---------- commands ----------

var toastTimer = null;
function toast(text, kind) {
  var el = document.querySelector('.toast');
  if (!el) {
    el = document.createElement('div');
    el.className = 'toast';
    document.querySelector('.widget-root').appendChild(el);
  }
  el.textContent = text;
  el.classList.toggle('is-info', kind === 'info');
  el.classList.add('is-visible');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(function () { el.classList.remove('is-visible'); }, 2500);
}

/** Sends a command to the hub; in demo mode applies a local stand-in so the preview reacts. */
function command(op, args, demoApply) {
  if (app.demo) {
    var result = demoApply ? demoApply() : null;
    setTimeout(render, 0);
    return Promise.resolve(result === undefined ? null : result);
  }
  if (!app.client) return Promise.reject(new Error('not_connected'));
  return app.client.request(op, args || {}).catch(function (err) {
    toast(t('actionFailed'));
    throw err;
  });
}

function bindControls() {
  $('btn-mute').addEventListener('click', function () {
    command('voice.toggleMute', null, function () { app.data.settings.mute = !app.data.settings.mute; }).catch(function () {});
  });
  $('btn-deaf').addEventListener('click', function () {
    command('voice.toggleDeaf', null, function () { app.data.settings.deaf = !app.data.settings.deaf; }).catch(function () {});
  });
  $('btn-mode').addEventListener('click', function () {
    var next = app.data.settings.mode === 'PUSH_TO_TALK' ? 'VOICE_ACTIVITY' : 'PUSH_TO_TALK';
    command('voice.setMode', { mode: next }, function () { app.data.settings.mode = next; }).catch(function () {});
  });
  // Leaving a call is the one action that is hard to undo: press and hold (unless switched off).
  VoicedeckMotion.holdToConfirm($('btn-leave'), {
    duration: 1200,
    resetAfter: 900,
    enabled: confirmLeaveEnabled,
    onConfirm: doLeave,
    onShortTap: function () { toast(t('holdToLeave'), 'info'); }
  });
  $('btn-leave').addEventListener('click', function () {
    if (!confirmLeaveEnabled()) doLeave();
  });

  var tabs = document.querySelectorAll('.tab');
  for (var i = 0; i < tabs.length; i++) {
    tabs[i].addEventListener('click', function (e) {
      app.tab = e.currentTarget.dataset.tab;
      render();
    });
  }
  $('open-channels').addEventListener('click', function () { app.tab = 'channels'; render(); });
  bindChat();
  window.addEventListener('resize', function () {
    render();
    if (mediaBackground) mediaBackground.layout();
  });
  $('state-action').addEventListener('click', function () {
    if (app.client && app.connection === 'pair_denied') app.client.requestPairing();
  });

  // member popup
  $('popup-close').addEventListener('click', closePopup);
  $('member-popup').addEventListener('click', function (e) { if (e.target === e.currentTarget) closePopup(); });
  var slider = $('popup-volume');
  var lastSent = 0, sendTimer = null;
  function sendVolume() {
    var userId = app.popupUserId, volume = Number(slider.value);
    lastSent = Date.now();
    command('user.setVolume', { userId: userId, volume: volume }, function () { findMember(userId).volume = volume; }).catch(function () {});
  }
  slider.addEventListener('pointerdown', function () { app.popupDragging = true; });
  slider.addEventListener('input', function () {
    // Detents: the handle sticks to 50/100/150/200 % when it comes within 9 % (4.5 % of the range).
    var snapped = VoicedeckMotion.snapToDetent(Number(slider.value), VOLUME_DETENTS, 9);
    if (snapped !== Number(slider.value)) slider.value = String(snapped);
    showVolume(snapped);
    clearTimeout(sendTimer);
    if (Date.now() - lastSent > 150) sendVolume();
    else sendTimer = setTimeout(sendVolume, 150);
  });
  slider.addEventListener('change', function () {
    app.popupDragging = false;
    clearTimeout(sendTimer);
    sendVolume();
  });
  $('popup-mute').addEventListener('click', function () {
    var m = findMember(app.popupUserId);
    if (!m) return;
    var next = !m.localMute;
    command('user.setMute', { userId: m.id, mute: next }, function () { m.localMute = next; }).catch(function () {});
  });
  $('popup-reset').addEventListener('click', function () {
    var m = findMember(app.popupUserId);
    if (!m) return;
    command('user.reset', { userId: m.id }, function () { m.volume = 100; m.localMute = false; }).catch(function () {});
  });
}

// ---------- member popup ----------

function findMember(userId) {
  if (!app.data || !app.data.voice) return null;
  for (var i = 0; i < app.data.voice.members.length; i++) {
    if (app.data.voice.members[i].id === userId) return app.data.voice.members[i];
  }
  return null;
}

var VOLUME_DETENTS = [50, 100, 150, 200];

function showVolume(volume) {
  $('popup-volume-value').textContent = volume + '%' + (volume === 100 ? ' · ' + t('volumeDefault') : '');
  $('popup-volume').style.setProperty('--fill', (volume / 2) + '%');
}

function openPopup(userId) {
  app.popupUserId = userId;
  app.popupDragging = false;
  $('member-popup').hidden = false;
  syncPopup(true);
}

function closePopup() {
  app.popupUserId = null;
  $('member-popup').hidden = true;
}

/** Keeps the open popup in line with the latest state; closes it if the person left. */
function syncPopup(initial) {
  if (!app.popupUserId) return;
  var m = findMember(app.popupUserId);
  if (!m) { closePopup(); return; }
  fillAvatar($('popup-avatar'), m);
  $('popup-name').textContent = m.name;
  if (initial || !app.popupDragging) {
    $('popup-volume').value = String(m.volume);
    showVolume(m.volume);
  }
  var muteBtn = $('popup-mute');
  muteBtn.textContent = m.localMute ? t('localUnmute') : t('localMute');
  muteBtn.classList.toggle('is-on', m.localMute);
}

// ---------- boot ----------

bindControls();
$('state-title').textContent = t('connectingHub'); // until boot decides between iCUE, preview and browser

// A single false read of iCUE_initialized is a race, not proof of a plain browser: retry ~1.5 s.
var bootAttempts = 0, BOOT_RETRY_MS = 100, BOOT_RETRY_MAX = 15;
function bootCheck() {
  if (typeof iCUE_initialized !== 'undefined' && iCUE_initialized) { onIcueInitialized(); return; }
  if (bootAttempts < BOOT_RETRY_MAX) { bootAttempts++; setTimeout(bootCheck, BOOT_RETRY_MS); return; }
  // Plain browser (development): ?demo shows sample data, otherwise talk to the real hub.
  start(/[?&]demo\b/.test(location.search));
}
bootCheck();
