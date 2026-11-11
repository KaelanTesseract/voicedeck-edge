// Soundboard: Discord's sounds as tiles, grouped like in Discord (favourites, built-in, per server).
// The list comes from the hub each time the window opens and lives only in memory; favourites
// (guild:sound ids) are kept per widget in localStorage.
var Soundboard = { sounds: null, loading: false, open: false };

var SOUNDBOARD_BUILT_IN = '0';
var MAX_SOUND_FAVORITES = 24;

function soundKey(s) { return s.guildId + ':' + s.id; }

function soundFavorites() {
  var f = loadStore().soundFavorites;
  return Array.isArray(f) ? f : [];
}

function toggleSoundFavorite(s) {
  var list = soundFavorites();
  var key = soundKey(s);
  var i = list.indexOf(key);
  if (i >= 0) {
    list.splice(i, 1);
    toast(t('unpinned'), 'info');
  } else {
    list.push(key);
    if (list.length > MAX_SOUND_FAVORITES) list.shift();
    toast(t('pinned'), 'info');
  }
  saveStore({ soundFavorites: list });
  renderSoundboard();
}

function inVoiceChannel() {
  return !!(app.data && app.data.voice && app.data.voice.channel);
}

function openSoundboard() {
  Soundboard.open = true;
  $('soundboard-popup').hidden = false;
  $('soundboard-title').textContent = t('soundboard');
  loadSounds();
  renderSoundboard();
}

function closeSoundboard() {
  Soundboard.open = false;
  $('soundboard-popup').hidden = true;
}

/** Asked fresh on every opening: availability depends on the current voice channel. */
function loadSounds() {
  if (Soundboard.loading) return;
  Soundboard.loading = true;
  command('soundboard.list', null, function () { return VoicedeckDemo.soundboard(); })
    .then(function (data) { Soundboard.sounds = (data && data.sounds) || []; })
    .catch(function () { if (!Soundboard.sounds) Soundboard.sounds = []; })
    .then(function () { Soundboard.loading = false; renderSoundboard(); });
}

function playSound(s, tile) {
  if (!inVoiceChannel()) { toast(t('soundNeedsVoice'), 'info'); return; }
  if (!s.available) { toast(t('soundUnavailable'), 'info'); return; }
  tile.classList.remove('is-playing');
  void tile.offsetWidth; // restart the pulse
  tile.classList.add('is-playing');
  command('soundboard.play', { guildId: s.guildId, soundId: s.id }).catch(function () {});
}

function soundTile(s) {
  var tile = document.createElement('button');
  tile.className = 'sound-tile' + (s.available ? '' : ' is-unavailable');
  var face = document.createElement('span');
  face.className = 'sound-emoji';
  var url = s.emojiImage ? imageUrl(s.emojiImage) : null;
  if (url) {
    var img = document.createElement('img');
    img.className = 'emoji';
    img.alt = '';
    img.src = url;
    img.draggable = false;
    img.onerror = function () { this.replaceWith(document.createTextNode('🔊')); };
    face.appendChild(img);
  } else {
    face.textContent = s.emoji || '🔊';
  }
  tile.appendChild(face);
  var name = document.createElement('span');
  name.className = 'sound-name';
  name.textContent = s.name;
  tile.appendChild(name);
  if (soundFavorites().indexOf(soundKey(s)) >= 0) tile.appendChild(icon('i-star', 'sound-star'));
  // tap = play, press and hold = pin/unpin (same gesture as channel favourites)
  VoicedeckMotion.longPress(tile, function () { playSound(s, tile); }, function () { toggleSoundFavorite(s); });
  return tile;
}

function soundGroup(box, title, sounds) {
  if (!sounds.length) return;
  var h = document.createElement('div');
  h.className = 'channel-group';
  h.textContent = title;
  box.appendChild(h);
  var grid = document.createElement('div');
  grid.className = 'sound-grid';
  sounds.forEach(function (s) { grid.appendChild(soundTile(s)); });
  box.appendChild(grid);
}

function renderSoundboard() {
  if (!Soundboard.open) return;
  var box = $('soundboard-list');
  var keep = box.scrollTop;
  box.textContent = '';
  $('soundboard-hint').textContent = !inVoiceChannel() ? t('soundNeedsVoice') : '';

  if (Soundboard.sounds === null) {
    $('soundboard-hint').textContent = t('loading');
    return;
  }
  var all = Soundboard.sounds;
  var favs = soundFavorites();
  soundGroup(box, t('favorites'), favs.map(function (k) {
    return all.filter(function (s) { return soundKey(s) === k; })[0];
  }).filter(Boolean));
  soundGroup(box, t('soundsBuiltIn'), all.filter(function (s) { return s.guildId === SOUNDBOARD_BUILT_IN; }));

  // servers in the order of the server column
  var order = (typeof orderedGuilds === 'function' ? orderedGuilds() : []).map(function (g) { return g.id; });
  var guilds = [];
  all.forEach(function (s) { if (s.guildId !== SOUNDBOARD_BUILT_IN && guilds.indexOf(s.guildId) < 0) guilds.push(s.guildId); });
  guilds.sort(function (a, b) {
    var ia = order.indexOf(a), ib = order.indexOf(b);
    return (ia < 0 ? 999 : ia) - (ib < 0 ? 999 : ib);
  });
  guilds.forEach(function (gid) {
    var sounds = all.filter(function (s) { return s.guildId === gid; });
    soundGroup(box, sounds[0].guildName || t('server'), sounds);
  });
  if (!all.length) $('soundboard-hint').textContent = t('soundsEmpty');
  box.scrollTop = keep;
}

function bindSoundboard() {
  $('btn-soundboard').addEventListener('click', openSoundboard);
  $('soundboard-close').addEventListener('click', closeSoundboard);
  $('soundboard-popup').addEventListener('click', function (e) { if (e.target === e.currentTarget) closeSoundboard(); });
}
