// Chat view: the open text channel, read-only, updated live by the hub.
// Messages and images exist only in memory. Private mode hides them until the chat is tapped.
var Chat = {
  renderedChannel: null,
  renderedKey: null,
  messageCount: 0,
  unread: 0,
  pinned: true,          // reader is at the newest message (updated only by real scrolling)
  rowObserver: null,
  revealedUntil: 0,
  revealTimer: null,
  autoOpened: false
};

var REVEAL_MS = 15000;
var PINNED_PX = 24;            // "at the bottom" tolerance (New Items Pill)
var JUMBO_EMOJI_MAX = 3;       // messages of only 1–3 emojis show them large, like Discord

function chatEnabled() {
  return getIcueProperty('showChat') !== false;
}

/**
 * Private mode: the iCUE setting is the default, the eye button in the chat header overrides it on
 * the widget. When the iCUE setting is changed again, it wins.
 */
function privateMode() {
  var setting = getIcueProperty('privateChat') === true;
  var store = loadStore();
  if (store.privateSetting !== setting) {
    if (store.privateOverride !== undefined && store.privateOverride !== null) saveStore({ privateOverride: null });
    saveStore({ privateSetting: setting });
    return setting;
  }
  return typeof store.privateOverride === 'boolean' ? store.privateOverride : setting;
}

function togglePrivateMode() {
  var next = !privateMode();
  saveStore({ privateOverride: next });
  Chat.revealedUntil = 0;
  updatePrivacy();
}

function openChat(channelId) {
  if (!chatEnabled()) return;
  command('chat.open', { channelId: channelId }, function () {
    app.data.chat = VoicedeckDemo.chat(channelId);
    (app.data.dms ? app.data.dms.conversations : []).forEach(function (c) { if (c.channelId === channelId) c.unread = 0; });
  })
    .then(function () {
      saveStore({ lastChat: channelId });
      if (app.layout !== 'xl') app.tab = 'chat';
      render();
    })
    .catch(function () {});
}

/** Reopens the chat that was open last time, once per connection. Forgets it if Discord refuses. */
function autoOpenLastChat() {
  if (Chat.autoOpened || app.demo || !chatEnabled() || !isDiscordReady() || currentChatId()) return;
  Chat.autoOpened = true;
  var last = loadStore().lastChat;
  if (!last) return;
  app.client.request('chat.open', { channelId: last }).catch(function (err) {
    if (err && (err.message === 'invalid_args' || err.message === 'discord_error')) saveStore({ lastChat: null });
  });
}

// ---------- message content (never built from HTML strings) ----------

var CUSTOM_EMOJI = /<(a?):(\w+):(\d{15,21})>/g;

function plainText(text) {
  return text
    .replace(/<@!?&?\d+>/g, '@…')
    .replace(/<#\d+>/g, '#…')
    .replace(/<t:(\d+)(:\w)?>/g, function (_, s) { return new Date(Number(s) * 1000).toLocaleString(VoicedeckI18n.locale()); });
}

/** True for messages that consist of a few emojis only (custom or Unicode) – shown large. */
function isJumbo(text) {
  var rest = text.replace(CUSTOM_EMOJI, '').replace(/\s+/g, '');
  var custom = (text.match(CUSTOM_EMOJI) || []).length;
  var unicode = rest ? (rest.match(/\p{Extended_Pictographic}/gu) || []).length : 0;
  var onlyEmoji = !rest || rest.replace(/[\p{Extended_Pictographic}‍️\u{1f3fb}-\u{1f3ff}]/gu, '') === '';
  var total = custom + unicode;
  return onlyEmoji && total > 0 && total <= JUMBO_EMOJI_MAX;
}

/** Appends text with Discord's custom emojis as images (loaded through the hub). */
function appendContent(parent, text) {
  var last = 0, match;
  CUSTOM_EMOJI.lastIndex = 0;
  while ((match = CUSTOM_EMOJI.exec(text)) !== null) {
    if (match.index > last) parent.appendChild(document.createTextNode(plainText(text.slice(last, match.index))));
    var url = imageUrl('/img/emojis/' + (match[1] ? 'a/' : '') + match[3]);
    if (url) {
      var img = document.createElement('img');
      img.className = 'emoji';
      img.alt = ':' + match[2] + ':';
      img.title = ':' + match[2] + ':';
      img.src = url;
      img.draggable = false;
      img.onerror = function () { this.replaceWith(document.createTextNode(this.alt)); };
      parent.appendChild(img);
    } else {
      parent.appendChild(document.createTextNode(':' + match[2] + ':'));
    }
    last = CUSTOM_EMOJI.lastIndex;
  }
  if (last < text.length) parent.appendChild(document.createTextNode(plainText(text.slice(last))));
}

function formatTime(iso) {
  var d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  var now = new Date();
  var time = d.toLocaleTimeString(VoicedeckI18n.locale(), { hour: '2-digit', minute: '2-digit' });
  return d.toDateString() === now.toDateString()
    ? time
    : d.toLocaleDateString(VoicedeckI18n.locale(), { day: '2-digit', month: '2-digit' }) + ' ' + time;
}

function messageExtras(m) {
  var parts = [];
  if (m.attachments > 0) parts.push('📎 ' + m.attachments + ' ' + (m.attachments === 1 ? t('attachment') : t('attachments')));
  if (m.hasEmbed && !m.content && !(m.images && m.images.length)) parts.push('▣ ' + t('embed'));
  return parts.join('  ');
}

/** Reactions under a message like in Discord: emoji + count, highlighted when one of them is yours. */
function appendReactions(body, reactions) {
  if (!reactions || !reactions.length) return;
  var box = document.createElement('div');
  box.className = 'message-reactions';
  reactions.forEach(function (r) {
    var chip = document.createElement('span');
    chip.className = 'reaction' + (r.me ? ' is-mine' : '');
    var url = r.image ? imageUrl(r.image) : null;
    if (url) {
      var img = document.createElement('img');
      img.className = 'emoji reaction-emoji';
      img.alt = '';
      img.src = url;
      img.draggable = false;
      img.onerror = function () { this.remove(); };
      chip.appendChild(img);
    } else if (r.emoji) {
      var glyph = document.createElement('span');
      glyph.className = 'reaction-glyph';
      glyph.textContent = r.emoji;
      chip.appendChild(glyph);
    } else {
      return;
    }
    var count = document.createElement('span');
    count.className = 'reaction-count';
    count.textContent = String(r.count);
    chip.appendChild(count);
    box.appendChild(chip);
  });
  if (box.children.length) body.appendChild(box);
}

function appendImages(body, images) {
  if (!images || !images.length) return;
  var box = document.createElement('div');
  box.className = 'message-images' + (images.length > 1 ? ' is-grid' : '');
  images.forEach(function (im) {
    var url = imageUrl(im.path);
    if (!url) return;
    var frame = document.createElement('button');
    frame.className = 'message-image';
    if (im.width > 0 && im.height > 0) frame.style.aspectRatio = im.width + ' / ' + im.height;
    var img = document.createElement('img');
    img.alt = '';
    img.decoding = 'async';
    img.draggable = false;
    img.src = url;
    img.addEventListener('load', keepPinnedToBottom);
    VoicedeckMotion.blurUp(img);
    frame.appendChild(img);
    frame.addEventListener('click', function () { openLightbox(url); });
    box.appendChild(frame);
  });
  body.appendChild(box);
}

// ---------- rendering ----------

function isPinned(list) {
  return list.scrollHeight - list.scrollTop - list.clientHeight <= PINNED_PX;
}

/** Rows can grow after layout (images, emoji fonts): keep the newest message in view if the reader was there. */
function keepPinnedToBottom() {
  if (!Chat.pinned) return;
  var list = $('chat-messages');
  list.scrollTop = list.scrollHeight;
}

/** Watches the rendered rows; any late growth re-pins the view to the bottom. */
function observeRows(list) {
  if (typeof ResizeObserver !== 'function') return;
  if (!Chat.rowObserver) Chat.rowObserver = new ResizeObserver(function () { keepPinnedToBottom(); });
  Chat.rowObserver.disconnect();
  for (var i = 0; i < list.children.length; i++) Chat.rowObserver.observe(list.children[i]);
}

function renderChat() {
  if (!app.started || !app.data) return;
  var chat = app.data.chat || {};
  var open = !!chat.channelId;
  $('chat-header').hidden = !open;
  $('chat-body').hidden = !open;
  $('chat-empty').hidden = open;
  if (!open) {
    Chat.renderedChannel = null;
    Chat.renderedKey = null;
    updatePrivacy();
    return;
  }

  // Rebuild only when the chat itself changed (voice updates also call render()).
  var key = JSON.stringify([chat.channelId, chat.messages, app.hub && app.hub.imageKey, VoicedeckI18n.locale()]);
  if (key === Chat.renderedKey) { updatePrivacy(); return; }
  Chat.renderedKey = key;

  $('chat-name').textContent = chat.channelName || '';
  document.querySelector('.chat-header-icon use').setAttribute('href', chat.direct ? '#i-dm' : '#i-hash');
  var list = $('chat-messages');
  var switched = Chat.renderedChannel !== chat.channelId;
  var wasPinned = switched || Chat.pinned;
  var keepScroll = list.scrollTop;
  var added = switched ? 0 : Math.max(0, chat.messages.length - Chat.messageCount);
  list.textContent = '';

  if (!chat.messages.length) {
    var empty = document.createElement('p');
    empty.className = 'pane-hint';
    empty.textContent = t('noMessages');
    list.appendChild(empty);
  }

  var previous = null;
  chat.messages.forEach(function (m) {
    // Consecutive messages from the same person within 5 minutes are grouped like in Discord.
    var grouped = previous && previous.authorId === m.authorId && new Date(m.time) - new Date(previous.time) < 5 * 60000;
    var row = document.createElement('div');
    row.className = 'message' + (grouped ? ' is-grouped' : '');

    var avatar = document.createElement('div');
    avatar.className = 'avatar message-avatar';
    if (!grouped) fillAvatar(avatar, { id: m.authorId, name: m.author, avatar: m.avatar });
    row.appendChild(avatar);

    var body = document.createElement('div');
    body.className = 'message-body';
    if (!grouped) {
      var head = document.createElement('div');
      head.className = 'message-head';
      var author = document.createElement('span');
      author.className = 'message-author';
      author.textContent = m.author;
      var time = document.createElement('span');
      time.className = 'message-time';
      time.textContent = formatTime(m.time);
      head.appendChild(author);
      head.appendChild(time);
      body.appendChild(head);
    }
    if (m.content) {
      var content = document.createElement('div');
      content.className = 'message-text' + (isJumbo(m.content) ? ' is-jumbo' : '');
      appendContent(content, m.content);
      if (m.edited) {
        var edited = document.createElement('span');
        edited.className = 'message-edited';
        edited.textContent = ' ' + t('edited');
        content.appendChild(edited);
      }
      body.appendChild(content);
    }
    appendImages(body, m.images);
    appendReactions(body, m.reactions);
    var extras = messageExtras(m);
    if (extras) {
      var extra = document.createElement('div');
      extra.className = 'message-extra';
      extra.textContent = extras;
      body.appendChild(extra);
    }
    row.appendChild(body);
    list.appendChild(row);
    previous = m;
  });

  // New Items Pill: stay at the bottom if the reader was there, otherwise count what arrived.
  observeRows(list);
  if (wasPinned) {
    Chat.unread = 0;
    Chat.pinned = true;
    list.scrollTop = list.scrollHeight;
  } else {
    list.scrollTop = keepScroll;
    Chat.unread += added;
  }
  Chat.messageCount = chat.messages.length;
  Chat.renderedChannel = chat.channelId;
  updatePill();
  updatePrivacy();
}

function updatePill() {
  var pill = $('new-items-pill');
  var n = Chat.unread;
  pill.classList.toggle('is-visible', n > 0);
  pill.tabIndex = n > 0 ? 0 : -1;
  if (n > 0) {
    var text = (n > 99 ? '99+' : n) + ' ' + (n === 1 ? t('newMessage') : t('newMessages'));
    pill.querySelector('span').textContent = text;
    pill.setAttribute('aria-label', text);
  }
}

function jumpToNewest() {
  var list = $('chat-messages');
  Chat.unread = 0;
  Chat.pinned = true;
  updatePill();
  list.scrollTo({ top: list.scrollHeight, behavior: VoicedeckMotion.reduced ? 'auto' : 'smooth' });
}

function updatePrivacy() {
  var on = privateMode();
  var veiled = on && !!currentChatId() && Date.now() >= Chat.revealedUntil;
  $('view-chat').classList.toggle('is-private', veiled);

  var btn = $('chat-privacy');
  btn.setAttribute('aria-pressed', on ? 'true' : 'false');
  btn.classList.toggle('is-on', on);
  btn.querySelector('use').setAttribute('href', on ? '#i-eye-off' : '#i-eye');
  btn.querySelector('span').textContent = on ? t('privacyOn') : t('privacyOff');

  var veil = $('private-veil');
  veil.textContent = '';
  veil.hidden = !veiled;
  if (veiled) {
    var label = document.createElement('span');
    label.className = 'private-veil-label';
    label.appendChild(icon('i-eye'));
    label.appendChild(document.createTextNode(t('tapToReveal')));
    veil.appendChild(label);
  }
}

function revealChat() {
  Chat.revealedUntil = Date.now() + REVEAL_MS;
  updatePrivacy();
  clearTimeout(Chat.revealTimer);
  Chat.revealTimer = setTimeout(updatePrivacy, REVEAL_MS + 50);
}

// ---------- lightbox ----------

function openLightbox(url) {
  if ($('view-chat').classList.contains('is-private')) return;
  $('lightbox-img').src = url;
  $('lightbox').hidden = false;
  requestAnimationFrame(function () { $('lightbox').classList.add('is-open'); });
}

function closeLightbox() {
  var box = $('lightbox');
  box.classList.remove('is-open');
  setTimeout(function () { box.hidden = true; $('lightbox-img').removeAttribute('src'); }, VoicedeckMotion.reduced ? 0 : 200);
}

function bindChat() {
  $('private-veil').addEventListener('click', revealChat);
  $('chat-privacy').addEventListener('click', togglePrivateMode);
  $('chat-pick').addEventListener('click', function () { app.tab = 'channels'; render(); });
  $('new-items-pill').addEventListener('click', jumpToNewest);
  $('chat-messages').addEventListener('scroll', function () {
    Chat.pinned = isPinned(this);
    if (Chat.unread > 0 && Chat.pinned) { Chat.unread = 0; updatePill(); }
  });
  $('lightbox').addEventListener('click', closeLightbox);
}
