// Swipe left/right to change XENEON EDGE pages.
// iCUE hands every touch to interactive widgets, so the page swipe never reaches iCUE. The widget detects
// the swipe itself and asks the hub to press the page shortcut the user assigned in iCUE.
var VoicedeckPageSwipe = (function () {
  'use strict';

  var MIN_DISTANCE = 110;   // px, roughly a finger's width and a half
  var MAX_DURATION = 900;   // ms, slower drags are not swipes
  var DIRECTION_RATIO = 1.8; // horizontal must clearly win over vertical
  var IGNORE = 'input, .popup-backdrop'; // the volume slider drags sideways itself

  function init(options) {
    var start = null;
    var swallowClickUntil = 0;

    document.addEventListener('pointerdown', function (e) {
      start = null;
      if (!e.isPrimary || !options.enabled() || (e.target.closest && e.target.closest(IGNORE))) return;
      start = { x: e.clientX, y: e.clientY, t: performance.now(), id: e.pointerId };
    }, true);

    document.addEventListener('pointerup', function (e) {
      var s = start;
      start = null;
      if (!s || e.pointerId !== s.id || !options.enabled()) return;
      var dx = e.clientX - s.x;
      var dy = e.clientY - s.y;
      if (Math.abs(dx) < MIN_DISTANCE || Math.abs(dx) < Math.abs(dy) * DIRECTION_RATIO) return;
      if (performance.now() - s.t > MAX_DURATION) return;

      // finger moves left → content moves left → next page, like on a phone
      var dir = dx < 0 ? 'next' : 'prev';
      swallowClickUntil = performance.now() + 400;
      flash(dir);
      options.turn(dir);
    }, true);

    document.addEventListener('pointercancel', function () { start = null; }, true);

    // the swipe must not also count as a tap on whatever it started on
    document.addEventListener('click', function (e) {
      if (performance.now() < swallowClickUntil) {
        e.preventDefault();
        e.stopPropagation();
      }
    }, true);
  }

  function flash(dir) {
    var hint = document.getElementById('page-hint');
    if (!hint) return;
    hint.className = 'page-hint ' + dir;
    void hint.offsetWidth; // restart the animation
    hint.classList.add('show');
  }

  return { init: init };
})();
