// Background image from the iCUE "media-selector" control.
// Own implementation of the documented transform model: iCUE describes the user's placement
// relative to a base size (baseWidth × baseHeight) with positionX/Y, scale and angle; we map that
// onto the actual widget size with the smaller of the two axis ratios.
var VoicedeckMedia = (function () {
  var IMAGE_TYPES = /\.(png|jpe?g|webp|gif|bmp)$/i;

  function resolvePath(value) {
    if (!value) return '';
    if (typeof value === 'string') return value;
    return value.pathToAsset || value.path || value.value || '';
  }

  /** Normalises the media-selector value; null when nothing (usable) is selected. */
  function normalize(raw) {
    var path = resolvePath(raw);
    if (!path || !IMAGE_TYPES.test(path)) return null;
    var base = raw && typeof raw === 'object' ? raw : {};
    var num = function (v, d) { v = Number(v); return Number.isFinite(v) ? v : d; };
    return {
      path: path,
      baseWidth: num(base.baseWidth, 0) || num(base.baseSizeX, 0),
      baseHeight: num(base.baseHeight, 0) || num(base.baseSizeY, 0),
      scale: num(base.scale, 1),
      positionX: num(base.positionX, 0),
      positionY: num(base.positionY, 0),
      angle: num(base.angle, 0)
    };
  }

  function MediaBackground(container) {
    this.container = container;
    this.img = null;
    this.path = null;
    this.config = null;
  }

  MediaBackground.prototype.show = function (raw) {
    var config = normalize(raw);
    if (!config) { this.clear(); return; }
    this.config = config;
    if (config.path !== this.path) {
      this.clear();
      var self = this;
      var img = document.createElement('img');
      img.className = 'media-image';
      img.alt = '';
      img.decoding = 'async';
      img.onload = function () {
        self.layout();
        img.classList.add('is-loaded');
        self.container.parentElement.classList.add('has-media');
      };
      img.onerror = function () { self.clear(); }; // never block the widget because of a broken image
      var src = config.path;
      try { src = decodeURIComponent(src); } catch (e) { /* already plain */ }
      img.src = src; // iCUE hands over a path the widget page can load as-is
      this.container.appendChild(img);
      this.img = img;
      this.path = config.path;
      this.config = config;
    }
    this.layout();
  };

  /** Without a base size (e.g. browser tests) the image simply covers the widget. */
  MediaBackground.prototype.layout = function () {
    var img = this.img, c = this.config;
    if (!img || !c) return;
    var w = this.container.clientWidth, h = this.container.clientHeight;
    if (!c.baseWidth || !c.baseHeight || !img.naturalWidth) {
      img.style.cssText = 'width:100%;height:100%;object-fit:cover;';
      return;
    }
    var unit = Math.min(w / c.baseWidth, h / c.baseHeight);
    img.style.cssText =
      'width:' + img.naturalWidth + 'px;height:' + img.naturalHeight + 'px;' +
      'transform-origin:top left;' +
      'transform:translate(' + (c.positionX * unit) + 'px,' + (c.positionY * unit) + 'px) ' +
      'scale(' + (c.scale * unit) + ') ' +
      'translate(' + (img.naturalWidth / 2) + 'px,' + (img.naturalHeight / 2) + 'px) ' +
      'rotate(' + c.angle + 'deg) ' +
      'translate(' + (-img.naturalWidth / 2) + 'px,' + (-img.naturalHeight / 2) + 'px);';
  };

  MediaBackground.prototype.clear = function () {
    if (this.img) this.img.remove();
    this.container.parentElement.classList.remove('has-media');
    this.img = null;
    this.path = null;
    this.config = null;
  };

  return { MediaBackground: MediaBackground, normalize: normalize };
})();
