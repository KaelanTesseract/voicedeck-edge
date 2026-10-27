// Voicedeck Edge – live test of the voice functions through the hub.
// Needs: hub running, you in a Discord voice channel. Every change is reverted at the end.
// Prints only states, counts and volumes – no names, IDs or messages.
//
// Usage: node tools/voice-test.mjs <tokenFile> [--no-leave]

import { readFileSync } from 'node:fs';

const token = readFileSync(process.argv[2], 'utf8').trim();
const skipLeave = process.argv.includes('--no-leave');
const results = [];
const check = (name, ok, detail = '') => {
  results.push(ok);
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
};
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const ws = new WebSocket('ws://localhost:47820/ws');
const state = {};
const inbox = [];
let speakingEvents = 0;
const waiters = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.t === 'snapshot') Object.assign(state, m.data);
  if (m.t === 'state') state[m.section] = m.data;
  if (m.t === 'speaking') speakingEvents++;
  inbox.push(m);
  for (const w of [...waiters]) if (w.f(m)) { waiters.splice(waiters.indexOf(w), 1); w.r(m); }
};
const waitFor = (f, ms = 10000) => {
  const i = inbox.findIndex(f);
  if (i >= 0) return Promise.resolve(inbox.splice(i, 1)[0]);
  return new Promise((r) => {
    const w = { f, r };
    waiters.push(w);
    setTimeout(() => { if (waiters.includes(w)) { waiters.splice(waiters.indexOf(w), 1); r(null); } }, ms);
  });
};
/** Waits until a condition on the live state holds (or times out). */
const until = async (cond, ms = 8000) => {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (cond()) return true; await sleep(100); }
  return cond();
};
let id = 0;
const call = async (op, args = {}) => {
  const n = ++id;
  ws.send(JSON.stringify({ op, id: n, ...args }));
  return (await waitFor((m) => m.t === 'result' && m.id === n, 25000)) ?? { ok: false, error: 'timeout' };
};

await new Promise((r) => (ws.onopen = r));
ws.send(JSON.stringify({ op: 'hello', token, proto: 1 }));
await waitFor((m) => m.t === 'snapshot');

const channel = state.voice.channel;
check('in a voice channel', !!channel, channel ? `${state.voice.members.length} Personen, Verbindung ${state.voice.connection}` : 'bitte zuerst einem Sprachkanal beitreten');
if (!channel) process.exit(1);
check('channel has a server name', !!channel.guildName);
check('members have avatars or default', state.voice.members.every((m) => m.avatar === null || m.avatar.startsWith('/img/avatars/')));

// 1) speaking – user talks during the first seconds
console.log('…  Sprech-Anzeige: 12 Sekunden zuhören …');
await sleep(12000);
check('speaking events arrive', speakingEvents > 0, `${speakingEvents} Ereignisse`);

const original = { ...state.settings };

// 2) mute / deafen
let r = await call('voice.toggleMute');
check('toggle mute on', r.ok && await until(() => state.settings.mute !== original.mute));
r = await call('voice.toggleMute');
check('toggle mute back', r.ok && await until(() => state.settings.mute === original.mute));

r = await call('voice.toggleDeaf');
check('toggle deafen on', r.ok && await until(() => state.settings.deaf !== original.deaf));
r = await call('voice.toggleDeaf');
check('toggle deafen back', r.ok && await until(() => state.settings.deaf === original.deaf));

// 3) mode
const otherMode = original.mode === 'PUSH_TO_TALK' ? 'VOICE_ACTIVITY' : 'PUSH_TO_TALK';
r = await call('voice.setMode', { mode: otherMode });
check(`mode → ${otherMode}`, r.ok && await until(() => state.settings.mode === otherMode));
r = await call('voice.setMode', { mode: original.mode });
check(`mode back → ${original.mode}`, r.ok && await until(() => state.settings.mode === original.mode));

// 4) per-user volume (needs somebody else in the channel)
const other = state.voice.members.find((m) => m.id !== state.me?.id);
if (!other) {
  console.log('SKIP Lautstärke pro Person – niemand sonst im Kanal');
} else {
  const before = { volume: other.volume, localMute: other.localMute };
  console.log(`     Lautstärke vorher laut Discord: ${before.volume}`);
  r = await call('user.setVolume', { userId: other.id, volume: 150 });
  check('set volume 150', r.ok, r.error ?? '');
  await sleep(2500); // give Discord time to send its own VOICE_STATE_UPDATE
  const after = state.voice.members.find((m) => m.id === other.id);
  console.log(`     Lautstärke danach im Hub-Zustand: ${after?.volume}`);
  r = await call('user.setMute', { userId: other.id, mute: true });
  check('local mute', r.ok && await until(() => state.voice.members.find((m) => m.id === other.id)?.localMute === true));
  r = await call('user.setMute', { userId: other.id, mute: before.localMute });
  r = await call('user.setVolume', { userId: other.id, volume: before.volume });
  check('volume + mute restored', r.ok && await until(() => {
    const m = state.voice.members.find((x) => x.id === other.id);
    return m?.volume === before.volume && m?.localMute === before.localMute;
  }));
  const meSelf = await call('user.setVolume', { userId: state.me.id, volume: 120 });
  console.log(`     Eigene Lautstärke setzen: ${meSelf.ok ? 'erlaubt' : 'abgelehnt (' + meSelf.error + ')'}`);
}

// 5) leave and rejoin
if (!skipLeave) {
  r = await call('voice.leave');
  check('leave', r.ok && await until(() => state.voice.channel === null));
  r = await call('voice.join', { channelId: channel.id });
  check('rejoin', r.ok && await until(() => state.voice.channel?.id === channel.id, 15000), r.error ?? '');
  await until(() => state.voice.connection === 'VOICE_CONNECTED', 10000);
  check('connected again', state.voice.connection === 'VOICE_CONNECTED', state.voice.connection);
}

ws.close();
const failed = results.filter((x) => !x).length;
console.log(`\n${results.length - failed}/${results.length} Prüfungen bestanden`);
process.exit(failed ? 1 : 0);
