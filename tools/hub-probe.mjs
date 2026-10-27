// Voicedeck Edge – hub probe: behaves like a widget and checks the hub end to end.
// Prints only the *shape* of the data (keys, types, counts) – no names, IDs or messages.
//
// Usage: node tools/hub-probe.mjs [tokenFile]
//   tokenFile: where the device token is kept between runs (default: none = pair every time)

import { readFileSync, writeFileSync, existsSync } from 'node:fs';

const BASE = 'http://localhost:47820';
const tokenFile = process.argv[2];
const results = [];

const shape = (v) =>
  v === null ? 'null'
  : Array.isArray(v) ? { count: v.length, item: v.length ? shape(v[0]) : null }
  : typeof v === 'object' ? Object.fromEntries(Object.entries(v).map(([k, x]) => [k, shape(x)]))
  : typeof v;

const check = (name, ok, detail = '') => {
  results.push({ name, ok });
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${name}${detail ? ' – ' + detail : ''}`);
};

// ---- HTTP ----
const health = await fetch(`${BASE}/health`).then((r) => r.json());
check('health', health.app === 'voicedeck-edge-hub' && health.proto === 1);

const noKey = await fetch(`${BASE}/img/avatars/123456789012345678/0123456789abcdef0123456789abcdef`);
check('image without key is refused', noKey.status === 403, `HTTP ${noKey.status}`);

// ---- WebSocket ----
const ws = new WebSocket(`${BASE.replace('http', 'ws')}/ws`);
const inbox = [];
const waiters = [];
ws.onmessage = (e) => {
  const msg = JSON.parse(e.data);
  const i = waiters.findIndex((w) => w.match(msg));
  if (i >= 0) waiters.splice(i, 1)[0].resolve(msg);
  else inbox.push(msg);
};
const waitFor = (match, ms = 15000) => {
  const i = inbox.findIndex(match);
  if (i >= 0) return Promise.resolve(inbox.splice(i, 1)[0]);
  return new Promise((resolve, reject) => {
    const w = { match, resolve };
    waiters.push(w);
    setTimeout(() => { waiters.splice(waiters.indexOf(w), 1); reject(new Error('timeout')); }, ms);
  });
};
let nextId = 1;
const call = async (op, args = {}) => {
  const id = nextId++;
  ws.send(JSON.stringify({ op, id, ...args }));
  return waitFor((m) => m.t === 'result' && m.id === id, 30000);
};

await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
const saved = tokenFile && existsSync(tokenFile) ? readFileSync(tokenFile, 'utf8').trim() : '';
ws.send(JSON.stringify({ op: 'hello', token: saved, proto: 1 }));

const first = await waitFor((m) => ['welcome', 'pair_pending', 'error'].includes(m.t));
if (first.t === 'pair_pending') {
  console.log('…  Bitte am PC im Fenster "Widget erlauben?" auf "Erlauben" klicken (60 s).');
  const answer = await waitFor((m) => m.t === 'paired' || m.t === 'pair_denied', 65000);
  check('pairing allowed', answer.t === 'paired');
  if (answer.t !== 'paired') process.exit(1);
  check('device token is 64 hex chars', /^[0-9a-f]{64}$/.test(answer.token));
  if (tokenFile) writeFileSync(tokenFile, answer.token);
}
const welcome = first.t === 'welcome' ? first : await waitFor((m) => m.t === 'welcome');
check('welcome', welcome.proto === 1 && /^[0-9a-f]{32}$/.test(welcome.imageKey), `hub ${welcome.hubVersion}`);

const snapshot = await waitFor((m) => m.t === 'snapshot');
console.log('     snapshot shape:', JSON.stringify(shape(snapshot.data)));
check('snapshot has all sections', ['status', 'me', 'voice', 'settings', 'chat'].every((k) => k in snapshot.data));
check('discord status is ready', snapshot.data.status.discord === 'ready', snapshot.data.status.discord);

const unknown = await call('rpc.raw', { cmd: 'AUTHORIZE' });
check('unknown op is rejected', unknown.ok === false && unknown.error === 'unknown_op');

const badArgs = await call('voice.join', { channelId: '../../x' });
check('invalid args are rejected', badArgs.ok === false && badArgs.error === 'invalid_args');

const guilds = await call('guilds.list');
check('guilds.list', guilds.ok && guilds.data.guilds.length > 0, `${guilds.data?.guilds?.length} Server`);

const guild = guilds.data.guilds.find((g) => g.icon) ?? guilds.data.guilds[0];
const t0 = Date.now();
const channels = await call('channels.list', { guildId: guild.id });
check('channels.list', channels.ok, `${channels.data?.voice?.length} Voice, ${channels.data?.text?.length} Text, ${Date.now() - t0} ms`);
console.log('     channels shape:', JSON.stringify(shape(channels.data)));
const t1 = Date.now();
await call('channels.list', { guildId: guild.id });
check('channels.list is cached', Date.now() - t1 < 200, `${Date.now() - t1} ms`);

if (guild.icon) {
  const img = await fetch(`${welcome.imageBase}${guild.icon}?k=${welcome.imageKey}`);
  const bytes = (await img.arrayBuffer()).byteLength;
  check('server icon via hub', img.status === 200 && img.headers.get('content-type') === 'image/png' && bytes > 0, `${bytes} bytes`);
}

const text = channels.data.text[0];
if (text) {
  const open = await call('chat.open', { channelId: text.id });
  const chat = await waitFor((m) => m.t === 'state' && m.section === 'chat');
  check('chat.open', open.ok && chat.data.channelId === text.id, `${chat.data.messages.length} Nachrichten`);
  await call('chat.close');
}

ws.close();
const failed = results.filter((r) => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} Prüfungen bestanden`);
process.exit(failed ? 1 : 0);
