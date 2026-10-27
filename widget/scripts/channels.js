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
  renderedKey: null
};

var MAX_FAVORITES = 12;
var DM_HOME = '@dms'; // pseudo server: the direct-message list at the top of the server column

function directConversations() {
  return (app.data && app.data.dms && app.data.dms.conversations) || [];
}

function unreadDirect() {
  return directConversations().reduce(function (sum, c) { return sum + (c.unread || 0); }, 0);
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
        selectGuild(pick || (Channels.guilds[0] && Channels.guilds[0].id) || null);
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
  if (!app.started || !app.data) return;
  // Rebuild only when something visible changed: voice/speaking updates arrive constantly and a
  // rebuild in the middle of a long press would cancel it.
  var key = JSON.stringify([
    Channels.guilds, Channels.guildId, Channels.lists[Channels.guildId] || null,
    Channels.loadingChannels === Channels.guildId, favorites(), currentVoiceId(), currentChatId(),
    chatEnabled(), app.hub && app.hub.imageKey, directConversations()
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
    if (unread > 0) {
      var badge = document.createElement('span');
      badge.className = 'guild-badge';
      badge.textContent = unread > 99 ? '99+' : String(unread);
      home.appendChild(badge);
    }
    home.addEventListener('click', function () { selectGuild(DM_HOME); });
    box.appendChild(home);
    var divider = document.createElement('span');
    divider.className = 'guild-divider';
    box.appendChild(divider);
  }
  (Channels.guilds || []).forEach(function (g) {
    var btn = document.createElement('button');
    btn.className = 'guild' + (g.id === Channels.guildId ? ' is-active' : '');
    btn.title = g.name;
    var icon = document.createElement('div');
    icon.className = 'avatar guild-icon';
    fillAvatar(icon, { id: g.id, name: g.name, avatar: g.icon });
    btn.appendChild(icon);
    btn.addEventListener('click', function () { selectGuild(g.id, g.id === Channels.guildId); });
    box.appendChild(btn);
  });
  box.scrollTop = keepScroll;
}

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
    data.text.forEach(function (c) {
      box.appendChild(channelRow({ id: c.id, name: c.name, kind: 'text', guildId: gid }));
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
    if (c.unread > 0) {
      var badge = document.createElement('span');
      badge.className = 'dm-badge';
      badge.textContent = c.unread > 99 ? '99+' : String(c.unread);
      row.appendChild(badge);
    }
    if (isFavorite(c.channelId)) row.appendChild(icon('i-star', 'channel-star'));
    attachPress(row, { id: c.channelId, name: c.name, kind: 'dm', guildId: DM_HOME });
    box.appendChild(row);
  });
  box.scrollTop = keepScroll;
  if (!list.length) hint.textContent = t('dmEmpty');
}
