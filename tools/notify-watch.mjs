// Voicedeck Edge – watches what the hub reports about unread messages, like a second widget.
// Prints only counts (servers/channels/DMs with unread messages) – no names, IDs or texts.
//
// Usage: node tools/notify-watch.mjs [minutes]   (default 3; the hub asks once "Allow widget?")

const minutes = Number(process.argv[2] || 3);
const ws = new WebSocket('ws://localhost:47820/ws');
const sum = (o) => Object.values(o || {}).reduce((a, b) => a + b, 0);
const time = () => new Date().toLocaleTimeString();

function report(label, data) {
  if (label === 'unread') {
    console.log(`${time()} unread: ${Object.keys(data?.guilds || {}).length} Server, ${sum(data?.guilds)} Nachrichten, ${Object.keys(data?.channels || {}).length} Kanäle`);
  } else if (label === 'dms') {
    const list = data?.conversations || [];
    console.log(`${time()} dms: ${list.length} Unterhaltungen, ${list.reduce((a, c) => a + (c.unread || 0), 0)} ungelesen`);
  }
}

ws.onopen = () => ws.send(JSON.stringify({ op: 'hello', token: '', proto: 1 }));
ws.onmessage = (e) => {
  const msg = JSON.parse(e.data);
  if (msg.t === 'pair_denied') { console.log(`${time()} abgelehnt`); process.exit(1); }
  else if (msg.t === 'pair_pending') console.log(`${time()} Bitte am PC "Erlauben" klicken …`);
  else if (msg.t === 'snapshot') {
    console.log(`${time()} verbunden – Startzustand:`);
    report('unread', msg.data.unread);
    report('dms', msg.data.dms);
    console.log(`(sieht ${minutes} min lang zu)`);
  } else if (msg.t === 'state' && (msg.section === 'unread' || msg.section === 'dms')) report(msg.section, msg.data);
};
ws.onclose = () => { console.log(`${time()} Verbindung beendet`); process.exit(0); };
setTimeout(() => { console.log(`${time()} fertig`); ws.close(); }, minutes * 60000);
