const sheet = document.getElementById('sheet');
const railList = document.getElementById('rail-list');
const pop = document.getElementById('pop');
const popImg = document.getElementById('pop-img');
const reelStrip = pop.querySelector('.reel-strip');
const reelFilm = document.getElementById('reel-film');
const prevBtn = pop.querySelector('.pop-prev');
const nextBtn = pop.querySelector('.pop-next');

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const mqNarrow = matchMedia('(max-width: 767px)');
const mqMid = matchMedia('(max-width: 1100px)');
const mqPhoneLand = matchMedia('(orientation: landscape) and (max-height: 500px)'); // phone turned sideways
const isPhone = () => mqNarrow.matches || mqPhoneLand.matches;
const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
const cols = () => (mqNarrow.matches ? 2 : mqMid.matches ? 4 : 6);

let frames = [];
let by = new URLSearchParams(location.search).get('by') === 'location' ? 'location' : 'month';
let order = [];
let secEls = [];
let railItems = [];
let reelBtns = [];
let activeSec = -1;
let illuminate = true; // illumination plays once per page load, only before the first toggle
const seen = { month: new Set(), location: new Set() };

const el = (tag, cls, text) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
};

// ---------- grouping ----------

const byDate = (a, b) => (a.date ?? '￿').localeCompare(b.date ?? '￿') || a.hash.localeCompare(b.hash);

function group() {
  const map = new Map();
  for (const f of frames) {
    const k = (by === 'month' ? f.date?.slice(0, 7) : f.location) ?? null;
    if (!map.has(k)) map.set(k, []);
    map.get(k).push(f);
  }
  const newest = fs => fs.reduce((a, f) => (f.date && f.date > a ? f.date : a), '');
  const secs = [...map].map(([key, fs]) => ({ key, fs: fs.sort(byDate), newest: newest(fs) }));
  secs.sort((a, b) => {
    if (a.key === null || b.key === null) return (a.key === null) - (b.key === null);
    return by === 'month' ? b.key.localeCompare(a.key) : b.newest.localeCompare(a.newest);
  });
  for (const s of secs) {
    if (by === 'month') {
      const [y, m] = (s.key ?? '').split('-');
      s.year = y;
      s.short = s.key ? MONTHS[m - 1].slice(0, 3) : 'Undated';
      s.label = s.key ? `${MONTHS[m - 1]} ${y}` : 'Undated';
      s.lines = s.key ? [MONTHS[m - 1], y] : ['Undated'];
    } else {
      s.short = s.label = s.key ?? 'Unplaced';
      // "Kyoto, Japan" -> two lines
      s.lines = (s.key ?? 'Unplaced').split(/,\s*(.+)/).filter(Boolean);
    }
  }
  return secs;
}

function formatDate(d) {
  if (!d) return '';
  return `${MONTHS[+d.slice(5, 7) - 1]} ${+d.slice(8, 10)}, ${d.slice(0, 4)}`;
}

// ---------- rendering ----------

const loader = new IntersectionObserver(entries => {
  for (const e of entries) {
    if (!e.isIntersecting) continue;
    loader.unobserve(e.target);
    e.target.querySelectorAll('img[data-src]').forEach(img => {
      img.src = img.dataset.src;
      img.removeAttribute('data-src');
    });
  }
}, { rootMargin: '1200px 0px' });

const revealer = new IntersectionObserver(entries => {
  for (const e of entries) {
    if (!e.isIntersecting) continue;
    revealer.unobserve(e.target);
    reveal(e.target);
  }
}, { threshold: 0.25 });

function reveal(wrap) {
  seen[by].add(wrap.dataset.key);
  const strip = wrap.firstElementChild;
  const done = () => wrap.classList.remove('pending');
  strip.addEventListener('transitionend', done, { once: true });
  setTimeout(done, 1400);
  requestAnimationFrame(() => {
    strip.classList.remove('pre-l', 'pre-r');
    if (strip.classList.contains('dark')) setTimeout(() => strip.classList.remove('dark'), 700);
  });
}

function buildStrip(fs, start, n, key, dir) {
  const wrap = el('div', 'strip-wrap');
  wrap.dataset.key = key;
  const strip = el('div', 'strip');
  strip.style.setProperty('--n', n);
  const fresh = !seen[by].has(key);
  if (fresh) {
    wrap.classList.add('pending');
    strip.classList.add(dir === 'l' ? 'pre-l' : 'pre-r');
    if (illuminate) strip.classList.add('dark');
  }
  const nums = el('div', 'nums');
  const cells = el('div', 'cells');
  for (let j = 0; j < n; j++) {
    const cell = el('div', 'cell');
    cell.style.setProperty('--i', j);
    const f = fs[j];
    if (f) {
      const btn = el('button', 'frame');
      btn.type = 'button';
      btn.dataset.hash = f.hash;
      btn.setAttribute('aria-label', `Open frame ${start + j + 1}`);
      const img = el('img');
      img.alt = f.description ?? '';
      img.dataset.src = f.thumb;
      btn.append(img);
      cell.append(btn);
      nums.append(el('div', 'fno', `${start + j + 1}▸`));
    } else {
      cell.append(el('div', 'blank'));
      nums.append(el('div', 'fno'));
    }
    cells.append(cell);
  }
  // film counter above the top sprockets; top and bottom sprocket bands are the same height
  strip.append(nums, el('div', 'sprockets'), cells, el('div', 'sprockets'));
  wrap.append(strip);
  loader.observe(wrap);
  if (fresh) revealer.observe(wrap);
  return wrap;
}

function render() {
  loader.disconnect();
  revealer.disconnect();
  cool();
  const secs = group();
  order = secs.flatMap(s => s.fs);
  const n = cols();
  sheet.replaceChildren();
  secEls = [];

  if (!secs.length) sheet.append(el('p', 'empty-note', 'No frames yet.'));

  secs.forEach((s, si) => {
    const sec = el('section', 'sec');
    const head = el('header', 'roll');
    head.append(el('h2', null, s.label), el('span', null, `${s.fs.length} ${s.fs.length === 1 ? 'frame' : 'frames'}`));
    sec.append(head);
    for (let i = 0, r = 0; i < s.fs.length; i += n, r++) {
      sec.append(buildStrip(s.fs.slice(i, i + n), i, n, `${s.key}|${r}`, r % 2 ? 'r' : 'l'));
    }
    sheet.append(sec);
    secEls.push(sec);
  });

  renderRail(secs);
  renderReel(secs);
  activeSec = -1;
  updateActive();
}

function renderRail(secs) {
  railList.replaceChildren();
  railItems = [];
  let year = null;
  secs.forEach((s, i) => {
    if (by === 'month' && s.year && s.year !== year) {
      year = s.year;
      railList.append(el('li', 'yr', year));
    }
    const li = el('li');
    const b = el('button', null, s.short);
    b.type = 'button';
    b.addEventListener('click', () => secEls[i].scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth' }));
    li.append(b);
    railList.append(li);
    railItems.push(b);
  });
}

function updateActive() {
  const line = innerHeight * 0.3;
  let a = 0;
  secEls.forEach((s, i) => {
    if (s.getBoundingClientRect().top <= line) a = i;
  });
  if (a === activeSec) return;
  activeSec = a;
  railItems.forEach((b, i) => { if (i === a) b.setAttribute('aria-current', 'true'); else b.removeAttribute('aria-current'); });
  const it = railItems[a];
  if (it) railList.scrollTop = it.offsetTop - railList.clientHeight / 2 + it.offsetHeight / 2;
}

let ticking = false;
addEventListener('scroll', () => {
  if (ticking) return;
  ticking = true;
  requestAnimationFrame(() => { ticking = false; updateActive(); });
}, { passive: true });

// ---------- grouping toggle ----------

function syncToggle() {
  document.querySelectorAll('.by-toggle button').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.by === by)));
}

document.addEventListener('click', e => {
  const b = e.target.closest('.by-toggle button');
  if (!b || b.dataset.by === by) return;
  by = b.dataset.by;
  illuminate = false;
  const url = new URL(location.href);
  if (by === 'location') url.searchParams.set('by', 'location'); else url.searchParams.delete('by');
  history.replaceState(null, '', url);
  syncToggle();
  render();
  scrollTo(0, document.querySelector('.gallery-wrap').offsetTop);
});

// breakpoint change: relayout rows without replaying animation
[mqNarrow, mqMid].forEach(mq => mq.addEventListener('change', () => {
  illuminate = false;
  // row keys depend on column count, so mark every row of the new layout as seen
  seen.month.clear();
  seen.location.clear();
  const secs = group();
  const n = cols();
  secs.forEach(s => { for (let r = 0; r * n < s.fs.length; r++) seen[by].add(`${s.key}|${r}`); });
  render();
}));

// ---------- dock hover ----------

let scaled = [];
let warm = false;
let enterT = 0;
let leaveT = 0;

function clearDock() {
  scaled.forEach(f => { f.style.transform = ''; f.style.zIndex = ''; });
  scaled = [];
}

function dock(btn) {
  clearDock();
  const cells = [...btn.closest('.cells').children];
  const i = cells.indexOf(btn.parentElement);
  [[0, 1.15], [1, 1.07], [2, 1.03]].forEach(([d, s]) => {
    [i - d, i + d].forEach(k => {
      const f = cells[k]?.querySelector('.frame');
      if (!f) return;
      f.style.transform = `scale(${s})`;
      f.style.zIndex = 10 - d;
      scaled.push(f);
    });
  });
}

function cool() {
  clearTimeout(enterT);
  clearTimeout(leaveT);
  clearDock();
  warm = false;
}

sheet.addEventListener('pointerover', e => {
  if (e.pointerType !== 'mouse') return;
  const btn = e.target.closest('.frame');
  clearTimeout(leaveT);
  if (!btn) { leaveT = setTimeout(cool, 150); return; }
  clearTimeout(enterT);
  if (warm) dock(btn);
  else enterT = setTimeout(() => { warm = true; dock(btn); }, 100);
});
sheet.addEventListener('pointerleave', cool);

// ---------- reel ----------

function renderReel(secs) {
  reelFilm.replaceChildren();
  reelBtns = [];
  let idx = 0;
  for (const s of secs) {
    const tag = el('span', 'reel-sec');
    s.lines.forEach(l => tag.append(el('span', null, l)));
    tag.title = s.label;
    reelFilm.append(tag);
    for (const f of s.fs) {
      const b = el('button', 'reel-thumb');
      b.type = 'button';
      b.dataset.i = idx++;
      b.setAttribute('aria-label', `Go to frame ${idx}`);
      const img = el('img');
      img.alt = '';
      img.loading = 'lazy';
      img.src = f.thumb;
      b.append(img);
      reelFilm.append(b);
      reelBtns.push(b);
    }
  }
}

reelFilm.addEventListener('click', e => {
  const b = e.target.closest('.reel-thumb');
  if (b) show(+b.dataset.i);
});
reelStrip.addEventListener('wheel', e => {
  if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return;
  reelStrip.scrollLeft += e.deltaY;
  e.preventDefault();
}, { passive: false });

// ---------- popup ----------

let cur = -1;
let lastFocus = null;

// fill the screen with a compositor-only transform, no layout changes, so large images don't lag
function setImmersive(on) {
  const fr = pop.querySelector('.pop-frame');
  pop.classList.toggle('immersive', on);
  fr.style.transform = '';
  if (!on) return;
  // fit the image itself (on mobile the frame also holds text), scaling about the frame centre
  const r = popImg.getBoundingClientRect();
  const c = fr.getBoundingClientRect();
  const W = pop.clientWidth;
  const H = pop.clientHeight;
  const s = Math.min(W / r.width, H / r.height);
  const cx = c.left + c.width / 2;
  const cy = c.top + c.height / 2;
  const dx = W / 2 - cx - s * (r.left + r.width / 2 - cx);
  const dy = H / 2 - cy - s * (r.top + r.height / 2 - cy);
  fr.style.transform = `translate(${dx}px, ${dy}px) scale(${s})`;
}

addEventListener('resize', () => {
  if (pop.hidden || !pop.classList.contains('immersive')) return;
  setImmersive(!isPhone());
});

// phone popup layout: see the "phone popup" comment in Gallery.Index.css
function setPhoneLayout() {
  const f = order[cur];
  if (!f) return;
  const portraitPhoto = f.h > f.w;
  pop.dataset.side = (parseInt(f.hash.slice(0, 8), 16) >> 4) & 1 ? 'left' : 'right'; // sides layout: which side the cards go
  pop.dataset.mlayout = mqPhoneLand.matches ? (portraitPhoto ? 'sides' : 'stack') : (portraitPhoto ? 'stack' : 'beside');
}

[mqNarrow, mqPhoneLand].forEach(mq => mq.addEventListener('change', () => { if (!pop.hidden) setPhoneLayout(); }));

// let the browser pick med or full by viewport width and pixel density
function setSrc(img, f) {
  if (f.med) {
    img.sizes = '100vw';
    img.srcset = `${f.med} ${f.medW}w, ${f.full} ${f.w}w`;
  } else {
    img.removeAttribute('srcset');
    img.removeAttribute('sizes');
  }
  img.src = f.full;
}

// keep Image objects referenced so the fetch isn't cancelled, and decode ahead so the swap is instant
const preloaded = new Map();
function preloadAround(i) {
  for (const n of [i + 1, i - 1, i + 2, i - 2]) {
    const f = order[n];
    if (!f || preloaded.has(f.hash)) continue;
    const img = new Image();
    setSrc(img, f);
    img.decode().catch(() => {});
    preloaded.set(f.hash, img);
    if (preloaded.size > 12) preloaded.delete(preloaded.keys().next().value); // ponytail: oldest-first cap, not LRU
  }
}

function layoutFor(f) {
  const h = parseInt(f.hash.slice(0, 8), 16);
  return f.h > f.w ? ['bottom', 'top'][h % 2] : ['bottom', 'top', 'tl', 'tr', 'bl', 'br'][h % 6];
}

const stops = n => `${n} ${n === 1 ? 'stop' : 'stops'}`;

function filmLine(f) {
  const push = f.push ? `${f.push > 0 ? 'pushed' : 'pulled'} ${stops(Math.abs(f.push))}` : '';
  return [f.film, push].filter(Boolean).join(' \u00B7 ');
}

function show(i, instant) {
  cur = i;
  setImmersive(false);
  const f = order[i];
  pop.dataset.layout = layoutFor(f);
  // opening fades in from nothing; stepping keeps the old image up until the next one is decoded, so no white flash
  if (instant) popImg.classList.remove('ready');
  const next = new Image();
  setSrc(next, f);
  next.decode().catch(() => {}).then(() => {
    if (cur !== i) return; // user already moved on
    setSrc(popImg, f);
    popImg.classList.add('ready');
  });
  popImg.alt = f.description ?? '';
  const meta = [formatDate(f.date), f.location].filter(Boolean).join(' \u00B7 ');
  const film = filmLine(f);
  pop.querySelectorAll('.pop-meta').forEach(e => { e.textContent = meta; });
  pop.querySelectorAll('.pop-film').forEach(e => { e.textContent = film; });
  pop.querySelector('.pop-desc').textContent = f.description ?? '';
  pop.querySelector('.pop-info').hidden = !(meta || film || f.description);
  pop.querySelector('.pop-head').hidden = !(meta || film);
  pop.querySelector('.pop-top').hidden = !(meta || film);
  pop.dataset.orient = f.h > f.w ? 'portrait' : 'landscape';
  setPhoneLayout();
  pop.toggleAttribute('data-nodesc', !f.description);
  prevBtn.hidden = i === 0;
  nextBtn.hidden = i === order.length - 1;
  reelBtns.forEach((b, k) => { if (k === i) b.setAttribute('aria-current', 'true'); else b.removeAttribute('aria-current'); });
  const cb = reelBtns[i];
  if (cb) reelStrip.scrollTo({ left: cb.offsetLeft - reelStrip.clientWidth / 2 + cb.offsetWidth / 2, behavior: instant || reduceMotion ? 'auto' : 'smooth' });
  preloadAround(i);
  const url = new URL(location.href);
  url.hash = f.hash;
  history.replaceState(null, '', url);
}

function open(i) {
  lastFocus = document.activeElement;
  cool();
  pop.hidden = false;
  document.documentElement.style.overflow = 'hidden';
  show(i, true);
  pop.querySelector('.pop-x').focus();
}

function close() {
  pop.hidden = true;
  document.documentElement.style.overflow = '';
  const url = new URL(location.href);
  url.hash = '';
  history.replaceState(null, '', url);
  lastFocus?.focus?.();
}

const step = d => { if (order[cur + d]) show(cur + d); };

sheet.addEventListener('click', e => {
  const btn = e.target.closest('.frame');
  if (btn) open(order.findIndex(f => f.hash === btn.dataset.hash));
});
pop.querySelector('.pop-x').addEventListener('click', close);
prevBtn.addEventListener('click', () => step(-1));
nextBtn.addEventListener('click', () => step(1));
pop.addEventListener('click', e => {
  if (e.target.closest('.pop-frame img, .pop-info')) {
    if (!isPhone()) setImmersive(!pop.classList.contains('immersive')); // no fullscreen on phones
  }
  else if (!e.target.closest('figure, button, .reel')) close();
});

document.addEventListener('keydown', e => {
  if (pop.hidden) return;
  if (e.key === 'Escape') close();
  else if (e.key === 'ArrowLeft') step(-1);
  else if (e.key === 'ArrowRight') step(1);
});

let touchX = null;
pop.addEventListener('touchstart', e => { touchX = e.touches[0].clientX; }, { passive: true });
pop.addEventListener('touchend', e => {
  if (touchX == null) return;
  const dx = e.changedTouches[0].clientX - touchX;
  touchX = null;
  if (Math.abs(dx) > 50) step(dx < 0 ? 1 : -1);
});

// ---------- boot ----------

syncToggle();
frames = await (await fetch('/gallery/data')).json();
render();
const deep = order.findIndex(f => f.hash === location.hash.slice(1));
if (deep >= 0) open(deep);
