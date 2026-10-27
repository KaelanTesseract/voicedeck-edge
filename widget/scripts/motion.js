// Micro-interactions, re-implemented in plain JS/CSS after the behaviour of interior.dev components
// (MIT, Copyright (c) 2026 ozzy – https://github.com/ddoemonn/interior): Press Depth, Hold to Confirm,
// Long Press, Segmented Control, Slider Detents, New Items Pill, Blur-up Image.
// No code is taken over; timings and values follow the components' documented behaviour.
var VoicedeckMotion = (function () {
  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function vibrate(ms) {
    try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) { /* not available on the Edge */ }
  }

  // ---------- Press Depth: the element sinks and leans toward the touch point ----------

  var PRESSABLE = '.control, .tab, .chip, .channel, .member:not(.is-self), .pill-button, .guild, .popup-close, .chat-privacy, .new-items-pill';
  var pressed = null;

  function setOrigin(el, e) {
    var r = el.getBoundingClientRect();
    var x = Math.max(-1, Math.min(1, ((e.clientX - r.left) / r.width) * 2 - 1));
    var y = Math.max(-1, Math.min(1, ((e.clientY - r.top) / r.height) * 2 - 1));
    el.style.setProperty('--press-x', x.toFixed(3));
    el.style.setProperty('--press-y', y.toFixed(3));
  }

  function endPress() {
    if (!pressed) return;
    pressed.el.classList.remove('is-pressed');
    pressed = null;
  }

  function initPressDepth() {
    document.addEventListener('pointerdown', function (e) {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      var el = e.target.closest && e.target.closest(PRESSABLE);
      if (!el || el.disabled) return;
      endPress();
      setOrigin(el, e);
      el.classList.add('is-pressed');
      pressed = { el: el, id: e.pointerId };
    }, true);
    window.addEventListener('pointermove', function (e) {
      if (!pressed || e.pointerId !== pressed.id) return;
      // Sliding off un-presses, sliding back on presses again.
      var r = pressed.el.getBoundingClientRect();
      var inside = e.clientX >= r.left && e.clientX <= r.right && e.clientY >= r.top && e.clientY <= r.bottom;
      pressed.el.classList.toggle('is-pressed', inside);
    }, true);
    ['pointerup', 'pointercancel'].forEach(function (t) { window.addEventListener(t, endPress, true); });
    window.addEventListener('blur', endPress);
    document.addEventListener('visibilitychange', function () { if (document.hidden) endPress(); });
  }

  // ---------- Hold to Confirm: a fill sweeps while held; releasing early drains it ----------

  /**
   * options: { duration, releaseRate, moveTolerance, resetAfter, enabled(): bool, onConfirm(), onShortTap() }
   * The element needs a child .hold-fill (the sweeping layer) – see index.html.
   */
  function holdToConfirm(el, options) {
    var duration = options.duration || 1200;
    var releaseRate = options.releaseRate || 2.5;
    var tolerance = options.moveTolerance || 10;
    var fill = el.querySelector('.hold-fill');
    var phase = 'idle', elapsed = 0, last = 0, origin = null, raf = 0, downAt = 0;

    function paint() {
      var v = Math.max(0, Math.min(1, elapsed / duration));
      fill.style.clipPath = 'inset(0 ' + ((1 - v) * 100).toFixed(2) + '% 0 0)';
    }

    function loop(now) {
      var dt = Math.min(64, now - last);
      last = now;
      if (phase === 'holding') {
        elapsed += dt;
        if (elapsed >= duration) {
          elapsed = duration;
          phase = 'committed';
          paint();
          el.classList.add('is-confirmed');
          vibrate(14);
          options.onConfirm();
          setTimeout(reset, options.resetAfter || 1200);
          return;
        }
      } else if (phase === 'releasing') {
        elapsed -= dt * releaseRate;
        if (elapsed <= 0) { elapsed = 0; phase = 'idle'; paint(); el.classList.remove('is-holding'); return; }
      } else {
        return;
      }
      paint();
      raf = requestAnimationFrame(loop);
    }

    function begin(e) {
      if (phase === 'committed' || phase === 'holding') return;
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      if (!options.enabled()) return; // plain tap mode, handled by click
      origin = { x: e.clientX, y: e.clientY };
      downAt = performance.now();
      try { el.setPointerCapture(e.pointerId); } catch (err) {}
      var wasIdle = phase === 'idle';
      phase = 'holding'; // resumes from the current fill when pressed again while draining
      el.classList.add('is-holding');
      if (wasIdle) { last = performance.now(); raf = requestAnimationFrame(loop); }
    }

    function release(e) {
      if (phase !== 'holding') return;
      phase = 'releasing';
      // A quick tap that never got anywhere: tell the user how this button works.
      if (e && e.type === 'pointerup' && performance.now() - downAt < 250 && options.onShortTap) options.onShortTap();
    }

    function reset() {
      cancelAnimationFrame(raf);
      phase = 'idle';
      elapsed = 0;
      paint();
      el.classList.remove('is-holding', 'is-confirmed');
    }

    el.addEventListener('pointerdown', begin);
    el.addEventListener('pointermove', function (e) {
      if (phase === 'holding' && origin && Math.hypot(e.clientX - origin.x, e.clientY - origin.y) > tolerance) release();
    });
    ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (t) { el.addEventListener(t, release); });
    el.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    window.addEventListener('blur', function () { release(); });
    paint();
    return { reset: reset };
  }

  // ---------- Long Press: stepped ink fill, a small pop when it fires ----------

  var LONG_PRESS_MS = 550, LONG_PRESS_STEPS = 12, LONG_PRESS_TOLERANCE = 8;

  /** Tap → onTap(), press and hold → onLongPress(). The element gets a .press-ink layer added. */
  function longPress(el, onTap, onLongPress) {
    var ink = document.createElement('span');
    ink.className = 'press-ink';
    ink.setAttribute('aria-hidden', 'true');
    el.appendChild(ink);
    var state = null;

    function set(step) {
      ink.style.clipPath = 'inset(0 ' + ((1 - step / LONG_PRESS_STEPS) * 100).toFixed(2) + '% 0 0)';
    }

    function end() {
      if (!state || state.fired) return;
      cancelAnimationFrame(state.raf);
      state = null;
      set(0);
    }

    el.addEventListener('pointerdown', function (e) {
      if (e.pointerType === 'mouse' && e.button !== 0) return;
      var start = performance.now();
      state = { x: e.clientX, y: e.clientY, fired: false, raf: 0 };
      var tick = function (now) {
        if (!state || state.fired) return;
        var step = Math.min(LONG_PRESS_STEPS, Math.floor(((now - start) / LONG_PRESS_MS) * LONG_PRESS_STEPS));
        set(step);
        if (step >= LONG_PRESS_STEPS) {
          state.fired = true;
          el.classList.add('is-fired');
          vibrate(12);
          onLongPress();
          setTimeout(function () { el.classList.remove('is-fired'); set(0); state = null; }, 260);
          return;
        }
        state.raf = requestAnimationFrame(tick);
      };
      state.raf = requestAnimationFrame(tick);
    });
    el.addEventListener('pointermove', function (e) {
      if (state && !state.fired && Math.hypot(e.clientX - state.x, e.clientY - state.y) > LONG_PRESS_TOLERANCE) end();
    });
    ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (t) { el.addEventListener(t, end); });
    el.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    el.addEventListener('click', function (e) {
      if (state && state.fired) { e.preventDefault(); return; }
      if (el.classList.contains('is-fired')) return;
      onTap();
    });
    set(0);
  }

  // ---------- Segmented Control: a thumb glides to the active tab ----------

  function placeThumb(container) {
    var thumb = container.querySelector('.tab-thumb');
    var active = container.querySelector('.tab.is-active:not([hidden])');
    if (!thumb) return;
    if (!active) { thumb.style.opacity = '0'; return; }
    var first = !thumb.dataset.placed;
    if (first || reduced) thumb.style.transition = 'none';
    thumb.style.opacity = '1';
    thumb.style.width = active.offsetWidth + 'px';
    thumb.style.height = active.offsetHeight + 'px';
    thumb.style.transform = 'translate(' + active.offsetLeft + 'px,' + active.offsetTop + 'px)';
    if (first || reduced) { void thumb.offsetWidth; thumb.style.transition = ''; }
    thumb.dataset.placed = '1';
  }

  // ---------- Slider Detents: the volume handle sticks to preset values ----------

  /** Returns the value snapped to the nearest detent within `pull`, else the value itself. */
  function snapToDetent(value, detents, pull) {
    var best = null;
    detents.forEach(function (d) {
      var dist = Math.abs(value - d);
      if (dist <= pull && (best === null || dist < Math.abs(value - best))) best = d;
    });
    return best === null ? value : best;
  }

  // ---------- Blur-up Image: develops from blurred to sharp once decoded ----------

  function blurUp(img) {
    img.classList.add('blur-up');
    function show(instant) {
      if (instant) img.classList.add('is-instant');
      img.classList.add('is-ready');
    }
    if (img.complete && img.naturalWidth > 0) { show(true); return; }
    img.addEventListener('load', function () {
      var p = img.decode ? img.decode() : Promise.resolve();
      p.then(function () { show(false); }, function () { img.classList.add('is-broken'); });
    }, { once: true });
    img.addEventListener('error', function () { img.classList.add('is-broken'); }, { once: true });
  }

  initPressDepth();

  return {
    reduced: reduced,
    holdToConfirm: holdToConfirm,
    longPress: longPress,
    placeThumb: placeThumb,
    snapToDetent: snapToDetent,
    blurUp: blurUp
  };
})();
