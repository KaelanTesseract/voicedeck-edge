// Connection to the Voicedeck Edge Hub on this PC (ws://localhost:47820).
// Handles hello/pairing, reconnects on its own and matches command results by id.
// Connection states: connecting → (unreachable) → pair_pending / pair_denied → ready → lost → …
var VoicedeckHubClient = (function () {
  var HUB_URL = 'ws://localhost:47820/ws';
  var PROTOCOL = 1;
  var REQUEST_TIMEOUT_MS = 15000;
  var RETRY_FAST_MS = 2000;
  var RETRY_SLOW_MS = 5000;

  function HubClient(options) {
    this.loadToken = options.loadToken;   // () => string
    this.saveToken = options.saveToken;   // (token) => void
    this.onState = options.onState;       // (connectionState) => void
    this.onMessage = options.onMessage;   // (message) => void
    this.state = 'idle';
    this.everReady = false;
    this.socket = null;
    this.nextId = 1;
    this.pending = {};
    this.retryTimer = null;
    this.stopped = false;
  }

  HubClient.prototype.start = function () {
    this.stopped = false;
    this.connect();
  };

  HubClient.prototype.stop = function () {
    this.stopped = true;
    clearTimeout(this.retryTimer);
    if (this.socket) this.socket.close();
  };

  HubClient.prototype.setState = function (state) {
    if (this.state === state) return;
    this.state = state;
    this.onState(state);
  };

  HubClient.prototype.connect = function () {
    var self = this;
    clearTimeout(this.retryTimer);
    if (!this.everReady) this.setState('connecting');

    var socket;
    try {
      socket = new WebSocket(HUB_URL);
    } catch (e) {
      // Blocked (e.g. permission revoked): treat like an unreachable hub.
      this.scheduleRetry();
      return;
    }
    this.socket = socket;

    socket.onopen = function () {
      socket.send(JSON.stringify({ op: 'hello', token: self.loadToken() || '', proto: PROTOCOL }));
    };

    socket.onmessage = function (event) {
      var msg;
      try { msg = JSON.parse(event.data); } catch (e) { return; }
      self.handle(msg);
    };

    socket.onclose = function () {
      if (self.socket !== socket) return;
      self.socket = null;
      self.failPending();
      if (self.stopped) return;
      if (self.state === 'proto_mismatch') { self.scheduleRetry(RETRY_SLOW_MS * 6); return; }
      self.setState(self.everReady ? 'lost' : 'unreachable');
      self.scheduleRetry();
    };
  };

  HubClient.prototype.scheduleRetry = function (delay) {
    var self = this;
    if (this.stopped) return;
    if (!delay) delay = this.state === 'lost' ? RETRY_FAST_MS : RETRY_SLOW_MS;
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(function () { self.connect(); }, delay);
  };

  HubClient.prototype.handle = function (msg) {
    switch (msg.t) {
      case 'pair_pending': this.setState('pair_pending'); break;
      case 'pair_denied': this.setState('pair_denied'); break;
      case 'paired': if (typeof msg.token === 'string') this.saveToken(msg.token); break;
      case 'welcome':
        this.everReady = true;
        this.setState('ready');
        break;
      case 'error':
        if (msg.code === 'proto_mismatch') this.setState('proto_mismatch');
        break;
      case 'result':
        var p = this.pending[msg.id];
        if (p) {
          delete this.pending[msg.id];
          clearTimeout(p.timer);
          if (msg.ok) p.resolve(msg.data); else p.reject(new Error(msg.error || 'failed'));
        }
        return;
    }
    this.onMessage(msg);
  };

  /** Sends a command; resolves with its data or rejects with the hub's error code. */
  HubClient.prototype.request = function (op, args) {
    var self = this;
    return new Promise(function (resolve, reject) {
      if (!self.socket || self.state !== 'ready' || self.socket.readyState !== WebSocket.OPEN) {
        reject(new Error('not_connected'));
        return;
      }
      var id = self.nextId++;
      var payload = { op: op, id: id };
      for (var key in args) if (Object.prototype.hasOwnProperty.call(args, key)) payload[key] = args[key];
      var timer = setTimeout(function () {
        delete self.pending[id];
        reject(new Error('timeout'));
      }, REQUEST_TIMEOUT_MS);
      self.pending[id] = { resolve: resolve, reject: reject, timer: timer };
      self.socket.send(JSON.stringify(payload));
    });
  };

  /** After a denial: ask the hub to show the "Allow widget?" window again. */
  HubClient.prototype.requestPairing = function () {
    if (this.socket && this.socket.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify({ op: 'pair.request' }));
      this.setState('pair_pending');
    }
  };

  HubClient.prototype.failPending = function () {
    for (var id in this.pending) {
      clearTimeout(this.pending[id].timer);
      this.pending[id].reject(new Error('not_connected'));
    }
    this.pending = {};
  };

  return HubClient;
})();
