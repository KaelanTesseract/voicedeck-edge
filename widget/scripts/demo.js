// Fictional sample data for the iCUE preview and for layout work in a browser (?demo).
// No real people, no network access.
var VoicedeckDemo = (function () {
  var GUILDS = [
    { id: '300000000000000001', name: 'Game Night', icon: null },
    { id: '300000000000000002', name: 'Maker Space', icon: null },
    { id: '300000000000000003', name: 'Study Group', icon: null }
  ];

  var CHANNELS = {
    '300000000000000001': {
      voice: [
        { id: '200000000000000001', name: 'Lounge', members: 6 },
        { id: '200000000000000002', name: 'Ranked', members: 0 },
        { id: '200000000000000003', name: 'AFK', members: 1 }
      ],
      text: [
        { id: '210000000000000001', name: 'general' },
        { id: '210000000000000002', name: 'clips' },
        { id: '210000000000000003', name: 'lfg' }
      ]
    },
    '300000000000000002': {
      voice: [{ id: '200000000000000011', name: 'Workbench', members: 2 }],
      text: [{ id: '210000000000000011', name: '3d-printing' }, { id: '210000000000000012', name: 'esp32' }]
    },
    '300000000000000003': {
      voice: [{ id: '200000000000000021', name: 'Focus Room', members: 0 }],
      text: [{ id: '210000000000000021', name: 'homework' }]
    }
  };

  function member(id, name, extra) {
    var m = {
      id: id, name: name, avatar: null, speaking: false,
      selfMute: false, selfDeaf: false, serverMute: false, serverDeaf: false,
      localMute: false, volume: 100
    };
    for (var k in extra) m[k] = extra[k];
    return m;
  }

  function minutesAgo(n) {
    return new Date(Date.now() - n * 60000).toISOString();
  }

  function message(id, authorId, author, minutes, content, extra) {
    var m = { id: id, authorId: authorId, author: author, avatar: null, content: content,
      time: minutesAgo(minutes), edited: false, attachments: 0, hasEmbed: false, images: [] };
    for (var k in extra) m[k] = extra[k];
    return m;
  }

  function snapshot() {
    return {
      status: { discord: 'ready' },
      me: { id: '100000000000000001', name: 'Alex', avatar: null },
      voice: {
        channel: { id: '200000000000000001', name: 'Lounge', guildId: '300000000000000001', guildName: 'Game Night' },
        connection: 'VOICE_CONNECTED',
        members: [
          member('100000000000000001', 'Alex', { speaking: true }),
          member('100000000000000002', 'Sam', { selfMute: true }),
          member('100000000000000003', 'Robin', { volume: 140 }),
          member('100000000000000004', 'Kim', { localMute: true }),
          member('100000000000000005', 'Jules', { selfDeaf: true, selfMute: true }),
          member('100000000000000006', 'Mika', {})
        ]
      },
      settings: {
        mute: false, deaf: false, mode: 'VOICE_ACTIVITY',
        input: { deviceId: 'default', volume: 80, devices: [
          { id: 'default', name: 'Default' },
          { id: 'mic-headset', name: 'Headset Microphone (USB Audio)' },
          { id: 'mic-studio', name: 'Studio Microphone' },
          { id: 'mic-webcam', name: 'Webcam Microphone' }
        ] },
        output: { deviceId: 'out-headset', volume: 100, devices: [
          { id: 'default', name: 'Default' },
          { id: 'out-headset', name: 'Headset Earphone (USB Audio)' },
          { id: 'out-speakers', name: 'Speakers (Realtek Audio)' },
          { id: 'out-monitor', name: 'Monitor (NVIDIA High Definition Audio)' }
        ] }
      },
      chat: chat('210000000000000001'),
      dms: {
        conversations: [
          { channelId: '600000000000000001', name: 'Robin', avatar: null, lastTime: minutesAgo(4), unread: 2 },
          { channelId: '600000000000000002', name: 'Kim', avatar: null, lastTime: minutesAgo(90), unread: 0 }
        ]
      },
      unread: {
        guilds: { '300000000000000001': 1, '300000000000000002': 4 },
        channels: { '210000000000000002': 1, '210000000000000011': 3, '210000000000000012': 1 }
      }
    };
  }

  function guilds() { return { guilds: GUILDS }; }

  function channels(guildId) {
    var c = CHANNELS[guildId] || { voice: [], text: [] };
    return { guildId: guildId, voice: c.voice, text: c.text };
  }

  function findChannel(id) {
    for (var g in CHANNELS) {
      var all = CHANNELS[g].voice.concat(CHANNELS[g].text);
      for (var i = 0; i < all.length; i++) if (all[i].id === id) return { channel: all[i], guildId: g };
    }
    return null;
  }

  function chat(channelId) {
    var found = findChannel(channelId);
    var dm = { '600000000000000001': 'Robin', '600000000000000002': 'Kim' }[channelId];
    return {
      channelId: channelId,
      direct: !!dm,
      channelName: dm || (found ? found.channel.name : 'general'),
      messages: [
        message('900000000000000001', '100000000000000003', 'Robin', 42, 'Who is up for a round later?'),
        message('900000000000000002', '100000000000000002', 'Sam', 40, 'Count me in, after dinner'),
        message('900000000000000003', '100000000000000002', 'Sam', 39, 'maybe 20:30?'),
        message('900000000000000004', '100000000000000006', 'Mika', 25, 'Sunset from the balcony', { images: [{ path: 'resources/demo/sunset.jpg', width: 640, height: 360 }] }),
        message('900000000000000007', '100000000000000006', 'Mika', 24, '', { attachments: 1 }),
        message('900000000000000008', '100000000000000003', 'Robin', 20, '🎉🔥'),
        message('900000000000000005', '100000000000000004', 'Kim', 12, 'Nice clip <@100000000000000006> 😄', { edited: true }),
        message('900000000000000006', '100000000000000001', 'Alex', 3, 'Joining the Lounge now')
      ]
    };
  }

  /** Demo stand-in for voice.join: move "me" into another channel with a few people. */
  function join(data, channelId) {
    var found = findChannel(channelId);
    if (!found) return;
    var guild = GUILDS.filter(function (g) { return g.id === found.guildId; })[0];
    data.voice.channel = { id: channelId, name: found.channel.name, guildId: found.guildId, guildName: guild ? guild.name : '' };
    data.voice.members = [member('100000000000000001', 'Alex', {})].concat(
      found.channel.id === '200000000000000001' ? snapshot().voice.members.slice(1) : []);
  }

  return { snapshot: snapshot, guilds: guilds, channels: channels, chat: chat, join: join };
})();
