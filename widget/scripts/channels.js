// Servers & channels view, plus pinned favourites.
// Lists come from the hub on demand (guilds.list / channels.list) and live only in memory;
// favourites (channel id, name, kind, server id) are kept per widget in localStorage.
var Channels = {
  guilds: null,          // [{ id, name, icon }]
  guildId: null,         // selected server
  lists: {},             // guildId -> { voice: [...], text: [...] }
  loadingGuilds: false,
  loadingChannels: null, // guildId being loaded
  renderedGuild: null,
  renderedKey: null,
  dragging: false        // a server is being moved: no rebuilds until it is dropped
};

var MAX_FAVORITES = 12;
var DM_HOME = '@dms'; // pseudo server: the direct-message list at the top of the server column

function directConversations() {
  return (app.data && app.data.dms && app.data.dms.conversations) || [];
}

function unreadDirect() {
  return directConversations().reduce(function (sum, c) { return sum + (c.unread || 0); }, 0);
}

/** Unread server messages the hub learned from notifications: { guilds: {id: n}, channels: {id: n} }. */
function serverUnread() {
  return (app.data && app.data.unread) || { guilds: {}, channels: {} };
}

function unreadServers() {
  var g = serverUnread().guilds || {};
  return Object.keys(g).reduce(function (sum, id) { return sum + (g[id] || 0); }, 0);
}

function badge(count, className) {
  var b = document.createElement('span');
  b.className = className;
  b.textContent = count > 99 ? '99+' : String(count);
  return b;
}

/** Servers in the order the user arranged in the widget; servers not arranged yet go to the end. */
function orderedGuilds() {
  var order = loadStore().guildOrder;
  if (!Array.isArray(order) || !Channels.guilds) return Channels.guilds || [];
  var rank = {};
  order.forEach(function (id, i) { rank[id] = i; });
  return Channels.guilds
    .map(function (g, i) { return { g: g, r: g.id in rank ? rank[g.id] : order.length + i }; })
    .sort(function (a, b) { return a.r - b.r; })
    .map(function (x) { return x.g; });
}

function favorites() {
  var f = loadStore().favorites;
  return Array.isArray(f) ? f : [];
}

function isFavorite(id) {
  return favorites().some(function (f) { return f.id === id; });
}

function toggleFavorite(item) {
  var list = favorites();
  var i = list.findIndex(function (f) { return f.id === item.id; });
  if (i >= 0) {
    list.splice(i, 1);
    toast(t('unpinned'), 'info');
  } else {
    list.push({ id: item.id, name: item.name, kind: item.kind, guildId: item.guildId });
    if (list.length > MAX_FAVORITES) list.shift();
    toast(t('pinned'), 'info');
  }
  saveStore({ favorites: list });
  renderChannels();
}

/** Loads the server list once per connection, then the channels of the chosen server. */
function ensureChannelsLoaded() {
  if (!app.data || !isDiscordReady()) return;
  if (Channels.guilds === null && !Channels.loadingGuilds) {
    Channels.loadingGuilds = true;
    command('guilds.list', null, function () { return VoicedeckDemo.guilds(); })
      .then(function (data) {
        Channels.guilds = (data && data.guilds) || [];
        var stored = loadStore().guildId;
        var voiceGuild = app.data.voice && app.data.voice.channel && app.data.voice.channel.guildId;
        var pick = [stored, voiceGuild].filter(function (id) {
          return id && (id === DM_HOME && chatEnabled() || Channels.guilds.some(function (g) { return g.id === id; }));
        })[0];
        var first = orderedGuilds()[0];
        selectGuild(pick || (first && first.id) || null);
      })
      .catch(function () { Channels.guilds = null; })
      .then(function () { Channels.loadingGuilds = false; renderChannels(); });
    return;
  }
  // The list of the selected server was dropped (e.g. after joining a channel): load it again.
  if (Channels.guildId && Channels.guildId !== DM_HOME && !Channels.lists[Channels.guildId] && Channels.loadingChannels !== Channels.guildId) {
    selectGuild(Channels.guildId);
  }
}

function selectGuild(guildId, force) {
  Channels.guildId = guildId;
  if (guildId) saveStore({ guildId: guildId });
  renderChannels();
  if (!guildId || guildId === DM_HOME || (Channels.lists[guildId] && !force) || Channels.loadingChannels === guildId) return;
  Channels.loadingChannels = guildId;
  command('channels.list', { guildId: guildId }, function () { return VoicedeckDemo.channels(guildId); })
    .then(function (data) { if (data) Channels.lists[guildId] = data; })
    .catch(function () {})
    .then(function () {
      if (Channels.loadingChannels === guildId) Channels.loadingChannels = null;
      renderChannels();
    });
}

/** Called when the hub connection is (re)established or Discord restarts: lists may be stale. */
function resetChannels() {
  Channels.guilds = null;
  Channels.lists = {};
  Channels.loadingGuilds = false;
  Channels.loadingChannels = null;
}

function joinVoice(channelId) {
  command('voice.join', { channelId: channelId }, function () { VoicedeckDemo.join(app.data, channelId); })
    .then(function () {
      if (app.layout === 'm') { app.tab = 'voice'; render(); }
      if (Channels.guildId && Channels.guildId !== DM_HOME) delete Channels.lists[Channels.guildId]; // member counts changed
    })
    .catch(function () {});
}

function onChannelTap(item) {
  if (item.kind === 'voice') joinVoice(item.id);
  else openChat(item.id);
}

/** Tap = use the channel, press and hold = pin/unpin (stepped ink fill, see motion.js). */
function attachPress(el, item) {
  VoicedeckMotion.longPress(el, function () { onChannelTap(item); }, function () { toggleFavorite(item); });
}

function renderChannels() {
  if (!app.started || !app.data || Channels.dragging) return;
  // Rebuild only when something visible changed: voice/speaking updates arrive constantly and a
  // rebuild in the middle of a long press would cancel it.
  var key = JSON.stringify([
    Channels.guilds, Channels.guildId, Channels.lists[Channels.guildId] || null,
    Channels.loadingChannels === Channels.guildId, favorites(), currentVoiceId(), currentChatId(),
    chatEnabled(), app.hub && app.hub.imageKey, directConversations(), serverUnread(), loadStore().guildOrder || null
  ]);
  if (key === Channels.renderedKey) return;
  Channels.renderedKey = key;
  renderGuilds();
  renderFavorites();
  renderChannelList();
}

function renderGuilds() {
  var box = $('guilds');
  var keepScroll = box.scrollTop;
  box.textContent = '';
  if (Channels.guilds && chatEnabled()) {
    // Direct messages first, like Discord's home button, with an unread badge.
    var home = document.createElement('button');
    home.className = 'guild guild-home' + (Channels.guildId === DM_HOME ? ' is-active' : '');
    home.title = t('directMessages');
    var homeIcon = document.createElement('div');
    homeIcon.className = 'avatar guild-icon';
    homeIcon.appendChild(icon('i-dm'));
    home.appendChild(homeIcon);
    var unread = unreadDirect();
    if (unread > 0) home.appendChild(badge(unread, 'guild-badge'));
    home.addEventListener('click', function () { selectGuild(DM_HOME); });
    box.appendChild(home);
    var divider = document.createElement('span');
    divider.className = 'guild-divider';
    box.appendChild(divider);
  }
  var unreadByGuild = serverUnread().guilds || {};
  orderedGuilds().forEach(function (g) {
    var btn = document.createElement('button');
    btn.className = 'guild' + (g.id === Channels.guildId ? ' is-active' : '');
    btn.title = g.name;
    btn.dataset.guildId = g.id;
    var icon = document.createElement('div');
    icon.className = 'avatar guild-icon';
    fillAvatar(icon, { id: g.id, name: g.name, avatar: g.icon });
    btn.appendChild(icon);
    if (unreadByGuild[g.id] > 0) btn.appendChild(badge(unreadByGuild[g.id], 'guild-badge'));
    btn.addEventListener('click', function (e) {
      if (btn.dataset.dropped) { delete btn.dataset.dropped; e.preventDefault(); return; }
      selectGuild(g.id, g.id === Channels.guildId);
    });
    attachGuildDrag(btn, box);
    box.appendChild(btn);
  });
  box.scrollTop = keepScroll;
}

/**
 * Press and hold a server, then drag it up or down to arrange the column like in Discord.
 * The order is saved per widget (server IDs only).
 */
var GUILD_HOLD_MS = 450;
var GUILD_SLOP_PX = 10;

function attachGuildDrag(btn, box) {
  var hold = null;

  function cancelHold() {
    if (hold) { clearTimeout(hold.timer); hold = null; }
  }

  btn.addEventListener('pointerdown', function (e) {
    if (!e.isPrimary || (e.pointerType === 'mouse' && e.button !== 0)) return;
    cancelHold();
    hold = { x: e.clientX, y: e.clientY, id: e.pointerId, active: false };
    hold.timer = setTimeout(function () {
      if (!hold) return;
      hold.active = true;
      Channels.dragging = true;
      btn.classList.add('is-dragging');
      box.classList.add('is-sorting');
      try { btn.setPointerCapture(hold.id); } catch (err) { /* pointer already gone */ }
    }, GUILD_HOLD_MS);
  });

  btn.addEventListener('pointermove', function (e) {
    if (!hold || e.pointerId !== hold.id) return;
    if (!hold.active) {
      // moving before the hold completes means scrolling or swiping, not sorting
      if (Math.abs(e.clientX - hold.x) > GUILD_SLOP_PX || Math.abs(e.clientY - hold.y) > GUILD_SLOP_PX) cancelHold();
      return;
    }
    var r = box.getBoundingClientRect();
    if (e.clientY < r.top + 40) box.scrollTop -= 8;
    else if (e.clientY > r.bottom - 40) box.scrollTop += 8;
    var siblings = Array.prototype.filter.call(box.querySelectorAll('.guild[data-guild-id]'), function (el) { return el !== btn; });
    var before = siblings.find(function (el) {
      var s = el.getBoundingClientRect();
      return e.clientY < s.top + s.height / 2;
    });
    if (before) { if (btn.nextSibling !== before) box.insertBefore(btn, before); }
    else if (siblings.length && box.lastElementChild !== btn) box.appendChild(btn);
  });

  function finish(e) {
    if (!hold || e.pointerId !== hold.id) return;
    var wasActive = hold.active;
    cancelHold();
    if (!wasActive) return;
    btn.classList.remove('is-dragging');
    box.classList.remove('is-sorting');
    btn.dataset.dropped = '1'; // the click that follows the drop must not select the server
    setTimeout(function () { delete btn.dataset.dropped; }, 400);
    var order = Array.prototype.map.call(box.querySelectorAll('.guild[data-guild-id]'), function (el) { return el.dataset.guildId; });
    Channels.dragging = false;
    saveStore({ guildOrder: order });
    toast(t('orderSaved'), 'info');
    renderChannels();
  }
  btn.addEventListener('pointerup', finish);
  btn.addEventListener('pointercancel', finish);
}

// While a server is being dragged the column must not scroll under the finger.
document.addEventListener('touchmove', function (e) {
  if (Channels.dragging && e.cancelable) e.preventDefault();
}, { passive: false });

function chip(item) {
  var btn = document.createElement('button');
  var current = item.kind === 'voice' ? currentVoiceId() : currentChatId();
  btn.className = 'chip' + (item.id === current ? ' is-current' : '');
  btn.appendChild(icon(item.kind === 'voice' ? 'i-speaker' : item.kind === 'dm' ? 'i-dm' : 'i-hash'));
  var name = document.createElement('span');
  name.textContent = item.name;
  btn.appendChild(name);
  attachPress(btn, item);
  return btn;
}

function renderFavorites() {
  var box = $('favorites');
  box.textContent = '';
  var list = favorites().filter(function (f) { return f.kind === 'voice' || chatEnabled(); });
  list.forEach(function (f) { box.appendChild(chip(f)); });
  box.hidden = list.length === 0;
}

function currentVoiceId() {
  return app.data && app.data.voice && app.data.voice.channel ? app.data.voice.channel.id : null;
}

function currentChatId() {
  return app.data && app.data.chat ? app.data.chat.channelId : null;
}

function channelRow(item, extra) {
  var row = document.createElement('button');
  var current = item.kind === 'voice' ? currentVoiceId() : currentChatId();
  row.className = 'channel' + (item.id === current ? ' is-current' : '');
  row.appendChild(icon(item.kind === 'voice' ? 'i-speaker' : 'i-hash'));
  var name = document.createElement('span');
  name.className = 'channel-name';
  name.textContent = item.name;
  row.appendChild(name);
  if (extra) row.appendChild(extra);
  if (isFavorite(item.id)) row.appendChild(icon('i-star', 'channel-star'));
  attachPress(row, item);
  return row;
}

function groupTitle(text) {
  var h = document.createElement('div');
  h.className = 'channel-group';
  h.textContent = text;
  return h;
}

function renderChannelList() {
  var box = $('channel-list');
  var hint = $('channel-hint');
  var keepScroll = Channels.renderedGuild === Channels.guildId ? box.scrollTop : 0;
  Channels.renderedGuild = Channels.guildId;
  box.textContent = '';
  hint.textContent = '';

  if (Channels.guilds === null || Channels.loadingChannels === Channels.guildId && !Channels.lists[Channels.guildId]) {
    hint.textContent = t('loading');
    return;
  }
  if (Channels.guildId === DM_HOME) { renderDirectList(box, hint, keepScroll); return; }
  if (Channels.guilds.length === 0) { hint.textContent = t('noGuilds'); return; }

  var data = Channels.lists[Channels.guildId];
  if (!data) return;
  var gid = Channels.guildId;

  if (data.voice.length) {
    box.appendChild(groupTitle(t('voiceChannels')));
    data.voice.forEach(function (c) {
      var count = null;
      if (c.members > 0) {
        count = document.createElement('span');
        count.className = 'channel-count';
        count.appendChild(icon('i-user'));
        count.appendChild(document.createTextNode(String(c.members)));
      }
      box.appendChild(channelRow({ id: c.id, name: c.name, kind: 'voice', guildId: gid }, count));
    });
  }
  if (chatEnabled() && data.text.length) {
    box.appendChild(groupTitle(t('textChannels')));
    var unreadByChannel = serverUnread().channels || {};
    data.text.forEach(function (c) {
      var count = unreadByChannel[c.id] > 0 ? badge(unreadByChannel[c.id], 'dm-badge') : null;
      var row = channelRow({ id: c.id, name: c.name, kind: 'text', guildId: gid }, count);
      if (count) row.classList.add('is-unread');
      box.appendChild(row);
    });
  }
  box.scrollTop = keepScroll;
  if (!box.children.length) hint.textContent = t('noChannels');
  else if (favorites().length === 0) hint.textContent = t('pinHint');
}

/** DM conversations the hub learned about from notifications since it connected (memory only). */
function renderDirectList(box, hint, keepScroll) {
  var list = directConversations();
  box.appendChild(groupTitle(t('directMessages')));
  list.forEach(function (c) {
    var row = document.createElement('button');
    row.className = 'channel channel-dm' + (c.channelId === currentChatId() ? ' is-current' : '') + (c.unread > 0 ? ' is-unread' : '');
    var avatar = document.createElement('div');
    avatar.className = 'avatar dm-avatar';
    fillAvatar(avatar, { id: c.channelId, name: c.name, avatar: c.avatar });
    row.appendChild(avatar);
    var name = document.createElement('span');
    name.className = 'channel-name';
    name.textContent = c.name;
    row.appendChild(name);
    var time = document.createElement('span');
    time.className = 'dm-time';
    time.textContent = formatTime(c.lastTime);
    row.appendChild(time);
    if (c.unread > 0) row.appendChild(badge(c.unread, 'dm-badge'));
    if (isFavorite(c.channelId)) row.appendChild(icon('i-star', 'channel-star'));
    attachPress(row, { id: c.channelId, name: c.name, kind: 'dm', guildId: DM_HOME });
    box.appendChild(row);
  });
  box.scrollTop = keepScroll;
  if (!list.length) hint.textContent = t('dmEmpty');
}
