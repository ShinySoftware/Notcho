/* Notcho, the browser copy. Peek, panel and HUD modes, eighteen tabs, the controls bar,
   the quick entry popup and Settings, plus the site's own animations at the bottom. */
(() => {
  'use strict';

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const rnd = (a, b) => a + Math.random() * (b - a);
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const uid = () => Math.random().toString(36).slice(2, 9);
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const store = {
    get(k, d) { try { const v = localStorage.getItem('notcho.' + k); return v ? JSON.parse(v) : d; } catch { return d; } },
    set(k, v) { try { localStorage.setItem('notcho.' + k, JSON.stringify(v)); } catch { /* storage unavailable */ } }
  };

  // ---------- icons (lucide's node data, rendered to strings) ----------
  const pascal = n => n.replace(/(^|-)([a-z0-9])/g, (_, __, c) => c.toUpperCase());
  function ic(name, cls = '') {
    const lib = window.lucide;
    const node = lib && lib.icons && lib.icons[pascal(name)];
    const kids = !node ? [] : node[0] === 'svg' ? node[2] || [] : node;
    const inner = kids.map(([t, a]) => `<${t} ${Object.entries(a).filter(([k]) => k !== 'key').map(([k, v]) => `${k}="${v}"`).join(' ')}/>`).join('');
    return `<svg class="${cls}" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${inner}</svg>`;
  }
  const fillIcons = (root = document) => $$('[data-ic]', root).forEach(el => { el.innerHTML = ic(el.dataset.ic); });

  // ---------- time helpers ----------
  const pad = n => String(n).padStart(2, '0');
  const mss = s => { s = Math.max(0, Math.round(s)); return `${Math.floor(s / 60)}:${pad(s % 60)}`; };
  const clock = d => d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const stamp = d => `${d.getHours()}.${pad(d.getMinutes())}.${pad(d.getSeconds())}`;
  const sameDay = (a, b) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  const dayStart = d => new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const addDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
  function ago(t) {
    const s = (Date.now() - t) / 1000;
    if (s < 60) return 'now';
    if (s < 3600) return `${Math.floor(s / 60)}m`;
    if (s < 86400) return `${Math.floor(s / 3600)}h`;
    return `${Math.floor(s / 86400)}d`;
  }
  function dayLabel(d) {
    const today = dayStart(new Date());
    if (sameDay(d, today)) return 'Today';
    if (sameDay(d, addDays(today, 1))) return 'Tomorrow';
    if (sameDay(d, addDays(today, -1))) return 'Yesterday';
    return d.toLocaleDateString([], { weekday: 'long' });
  }

  // ---------- toasts ----------
  function toast(msg, { icon = 'check', radar = false, ms = 2800 } = {}) {
    const el = document.createElement('div');
    el.className = 'toast';
    el.innerHTML = `${radar ? '<span class="radar"></span>' : ic(icon)}<span>${esc(msg)}</span>`;
    $('#toasts').appendChild(el);
    setTimeout(() => { el.classList.add('out'); setTimeout(() => el.remove(), 300); }, ms);
  }
  async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
  }

  // ---------- state ----------
  const TABS = [
    { id: 'media', icon: 'music', name: 'Now Playing', c: '#FF375F', side: 'l' },
    { id: 'weather', icon: 'cloud-sun', name: 'Weather', c: '#64D2FF', side: 'l' },
    { id: 'sports', icon: 'trophy', name: 'Sports', c: '#30D158', side: 'l' },
    { id: 'news', icon: 'newspaper', name: 'News', c: '#FF453A', side: 'l' },
    { id: 'rss', icon: 'rss', name: 'RSS', c: '#FF9F0A', side: 'l' },
    { id: 'tray', icon: 'inbox', name: 'File Tray', c: '#30D158', side: 'l' },
    { id: 'stats', icon: 'cpu', name: 'System', c: '#BF5AF2', side: 'l' },
    { id: 'processes', icon: 'layers', name: 'Processes', c: '#5E5CE6', side: 'l' },
    { id: 'capture', icon: 'scan', name: 'Capture', c: '#0A84FF', side: 'l' },
    { id: 'mirror', icon: 'square-user', name: 'Mirror', c: '#BF5AF2', side: 'l' },
    { id: 'notes', icon: 'audio-lines', name: 'Voice Notes', c: '#FF375F', side: 'r' },
    { id: 'tasks', icon: 'list-checks', name: 'Tasks', c: '#30D158', side: 'r' },
    { id: 'clipboard', icon: 'clipboard-list', name: 'Clipboard', c: '#64D2FF', side: 'r' },
    { id: 'calendar', icon: 'calendar-days', name: 'Calendar', c: '#FF453A', side: 'r' },
    { id: 'timer', icon: 'timer', name: 'Timer', c: '#FF9F0A', side: 'r' },
    { id: 'awake', icon: 'coffee', name: 'Keep Awake', c: '#FFD60A', side: 'r' },
    { id: 'calculator', icon: 'calculator', name: 'Calculator', c: '#FF9F0A', side: 'r' },
    { id: 'prompter', icon: 'align-left', name: 'Teleprompter', c: '#FFD60A', side: 'r' }
  ];
  let CAM_W = 180, WING_W = 104;
  const fitNotch = () => { const small = innerWidth < 640; CAM_W = small ? 80 : 180; WING_W = small ? 84 : 104; };
  fitNotch();

  const S = {
    mode: 'peek',
    tab: store.get('tab', 'media'),
    pinned: false,
    sticky: false,
    volume: 55,
    bright: 100,
    hidden: store.get('hidden', []),
    hoverDelay: store.get('hoverDelay', 150),
    unit: store.get('unit', 'auto'),
    hudVol: store.get('hudVol', true),
    hudBright: store.get('hudBright', true),
    opened: false
  };
  if (!TABS.some(t => t.id === S.tab)) S.tab = 'media';

  const notch = $('#notch');
  const L = { peek: $('#peekLayer'), panel: $('#panelLayer'), hud: $('#hudLayer') };
  let view = null;      // the mounted tab view
  let hudTimer = 0;
  let dragActive = false;

  // ======================================================================
  //  MEDIA
  // ======================================================================
  const TRACKS = [
    { t: 'Glass Harbour', a: 'Lumen Tide', al: 'Night Ferries', d: 214, c: '#c084fc', art: ['#f472b6', '#7c3aed', '#22d3ee'], k: 'sun' },
    { t: 'Paper Satellites', a: 'Odd Weather', al: 'Low Orbit', d: 187, c: '#38bdf8', art: ['#7dd3fc', '#1e3a8a', '#0c4a6e'], k: 'rings' },
    { t: 'Copper Morning', a: 'The Finch Avenue', al: 'Kettle Songs', d: 243, c: '#fb923c', art: ['#fdba74', '#c2410c', '#451a03'], k: 'wave' },
    { t: 'Slow Static', a: 'Marlowe & the Sea', al: 'Signal / Noise', d: 198, c: '#4ade80', art: ['#86efac', '#065f46', '#022c22'], k: 'grid' }
  ];
  const M = { i: 0, pos: 47, playing: true };
  function artBg(tr) {
    const [a, b, c] = tr.art;
    if (tr.k === 'sun') return `radial-gradient(circle at 50% 62%, ${a} 0 21%, transparent 22%), radial-gradient(circle at 50% 62%, transparent 0 30%, ${a}55 31% 33%, transparent 34%), linear-gradient(165deg, ${b}, ${c})`;
    if (tr.k === 'rings') return `repeating-radial-gradient(circle at 28% 72%, ${a} 0 5%, ${b} 5% 10%, ${c} 10% 15%)`;
    if (tr.k === 'wave') return `repeating-linear-gradient(-28deg, ${a} 0 9%, ${b} 9% 18%, ${c} 18% 27%)`;
    return `conic-gradient(from 45deg at 50% 50%, ${a}, ${b}, ${c}, ${a})`;
  }
  const track = () => TRACKS[M.i];
  function mediaSkip(dir) {
    if (dir < 0 && M.pos > 3) { M.pos = 0; }
    else { M.i = (M.i + dir + TRACKS.length) % TRACKS.length; M.pos = 0; }
    onMediaChanged(true);
  }
  function onMediaChanged(swap) {
    renderPeek();
    if (S.mode === 'panel') {
      if (S.tab === 'media') refresh(swap);
      renderControls();
    }
  }
  function mediaTick() {
    if (!M.playing) return;
    M.pos += 1;
    if (M.pos >= track().d) { mediaSkip(1); return; }
    const fill = $('#mFill');
    if (fill) {
      fill.style.width = (M.pos / track().d * 100) + '%';
      $('#mPos').textContent = mss(M.pos);
      $('#mRem').textContent = '-' + mss(track().d - M.pos);
    }
  }
  const mediaView = {
    html() {
      const tr = track();
      return `<div class="media" style="--accent:${tr.c}">
        <div class="m-art${this.swap ? ' swap' : ''}" style="background:${artBg(tr)}"></div>
        <div class="grow">
          <div class="row"><div class="grow"><div class="m-title ell">${esc(tr.t)}</div><div class="m-sub ell">${esc(tr.a)} — ${esc(tr.al)}</div></div>
            <span class="p-bars ${M.playing ? 'on' : ''}" style="--accent:${tr.c}"><i></i><i></i><i></i><i></i></span></div>
          <div class="m-scrub"><span id="mPos" class="tnum">${mss(M.pos)}</span><div class="m-track" data-act="mScrub"><div class="m-fill" id="mFill" style="width:${M.pos / tr.d * 100}%"></div></div><span id="mRem" class="tnum">-${mss(tr.d - M.pos)}</span></div>
          <div class="m-btns">
            <button type="button" data-act="mPrev" aria-label="Previous">${ic('rewind')}</button>
            <button type="button" class="big" data-act="mPlay" aria-label="${M.playing ? 'Pause' : 'Play'}">${ic(M.playing ? 'pause' : 'play')}</button>
            <button type="button" data-act="mNext" aria-label="Next">${ic('fast-forward')}</button>
          </div>
        </div></div>`;
    }
  };

  // ======================================================================
  //  WEATHER
  // ======================================================================
  const PLACES = [
    { n: 'Whitby', lat: 43.88, lon: -78.94, here: true, base: 6 },
    { n: 'Toronto', lat: 43.65, lon: -79.38, base: 7 },
    { n: 'London', lat: 51.51, lon: -0.13, base: 10 },
    { n: 'Tokyo', lat: 35.68, lon: 139.69, base: 14 }
  ];
  const W = { i: 0, data: {}, loading: {} };
  const useF = () => S.unit === 'f' || (S.unit === 'auto' && /^en-US$/i.test(navigator.language || ''));
  const deg = c => Math.round(useF() ? c * 9 / 5 + 32 : c) + '°';
  function wCode(code, day) {
    if (code === 0) return day ? { i: 'sun', l: 'Clear', y: true } : { i: 'moon-star', l: 'Clear' };
    if (code <= 2) return day ? { i: 'cloud-sun', l: 'Partly cloudy' } : { i: 'cloud-moon', l: 'Partly cloudy' };
    if (code === 3) return { i: 'cloud', l: 'Cloudy' };
    if (code <= 48) return { i: 'cloud-fog', l: 'Fog' };
    if (code <= 57) return { i: 'cloud-drizzle', l: 'Drizzle' };
    if (code <= 67 || (code >= 80 && code <= 82)) return { i: 'cloud-rain', l: 'Rain' };
    if (code <= 86) return { i: 'cloud-snow', l: 'Snow' };
    return { i: 'cloud-lightning', l: 'Thunderstorm' };
  }
  function sky(code, day) {
    if (!day) return 'linear-gradient(110deg,#1e2a5e,#121838 48%,#1c1c1e)';
    if (code <= 1) return 'linear-gradient(110deg,#2f6fd6,#21468f 46%,#1c1c1e)';
    if (code <= 3) return 'linear-gradient(110deg,#4e6688,#2b3a52 46%,#1c1c1e)';
    return 'linear-gradient(110deg,#3d5277,#253247 46%,#1c1c1e)';
  }
  const STOPS = [[-15, [94, 92, 230]], [-5, [100, 210, 255]], [5, [90, 200, 250]], [12, [99, 230, 160]], [20, [255, 214, 10]], [27, [255, 159, 10]], [35, [255, 69, 58]]];
  function tempColor(c) {
    const i = STOPS.findIndex(s => s[0] >= c);
    if (i <= 0) return `rgb(${STOPS[i < 0 ? STOPS.length - 1 : 0][1]})`;
    const [t0, c0] = STOPS[i - 1], [t1, c1] = STOPS[i];
    const f = (c - t0) / (t1 - t0);
    return `rgb(${c0.map((v, k) => Math.round(v + (c1[k] - v) * f))})`;
  }
  function seeded(seed) { let s = seed; return () => { s = (s * 9301 + 49297) % 233280; return s / 233280; }; }
  function fakeWeather(p) {
    const r = seeded(p.n.length * 977 + new Date().getDate());
    const now = new Date();
    const codes = [0, 1, 2, 3, 3, 61, 2, 0];
    const hours = Array.from({ length: 12 }, (_, k) => {
      const d = new Date(now.getTime() + k * 3600e3);
      const h = d.getHours();
      const t = p.base + 5 * Math.sin((h - 9) / 24 * Math.PI * 2) + r() * 1.5;
      return { h: k === 0 ? 'Now' : d.toLocaleTimeString([], { hour: 'numeric' }).replace(' ', ''), t, code: codes[Math.floor(r() * codes.length)], day: h >= 7 && h < 18 };
    });
    const days = Array.from({ length: 7 }, (_, k) => {
      const lo = p.base - 4 + r() * 5, hi = lo + 5 + r() * 6;
      return { d: k === 0 ? 'Today' : addDays(now, k).toLocaleDateString([], { weekday: 'short' }), lo, hi, code: codes[Math.floor(r() * codes.length)] };
    });
    days[0].lo = Math.min(days[0].lo, hours[0].t - 1); days[0].hi = Math.max(days[0].hi, hours[0].t + 1);
    return { now: { t: hours[0].t, code: hours[0].code, day: hours[0].day }, hours, days, live: false };
  }
  async function loadWeather(i) {
    const p = PLACES[i];
    if (W.data[p.n] || W.loading[p.n]) return;
    W.loading[p.n] = true;
    W.data[p.n] = fakeWeather(p);
    try {
      const url = `https://api.open-meteo.com/v1/forecast?latitude=${p.lat}&longitude=${p.lon}&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=7`;
      const res = await fetch(url);
      const j = await res.json();
      const nowKey = j.current.time.slice(0, 13);
      const start = Math.max(0, j.hourly.time.findIndex(t => t.slice(0, 13) === nowKey));
      const hours = Array.from({ length: 12 }, (_, k) => {
        const idx = start + k;
        const hr = Number(j.hourly.time[idx].slice(11, 13));
        const label = k === 0 ? 'Now' : `${hr % 12 || 12}${hr < 12 ? 'AM' : 'PM'}`;
        return { h: label, t: k === 0 ? j.current.temperature_2m : j.hourly.temperature_2m[idx], code: k === 0 ? j.current.weather_code : j.hourly.weather_code[idx], day: !!(k === 0 ? j.current.is_day : j.hourly.is_day[idx]) };
      });
      const days = j.daily.time.map((t, k) => ({
        d: k === 0 ? 'Today' : new Date(t + 'T12:00').toLocaleDateString([], { weekday: 'short' }),
        lo: j.daily.temperature_2m_min[k], hi: j.daily.temperature_2m_max[k], code: j.daily.weather_code[k]
      }));
      W.data[p.n] = { now: { t: j.current.temperature_2m, code: j.current.weather_code, day: !!j.current.is_day }, hours, days, live: true };
    } catch { /* offline or blocked: keep the sample forecast */ }
    W.loading[p.n] = false;
    if (i === 0) renderPeek();
    if (S.mode === 'panel' && S.tab === 'weather' && W.i === i) refresh();
  }
  const weatherView = {
    html() {
      const p = PLACES[W.i];
      const w = W.data[p.n] || fakeWeather(p);
      const cur = wCode(w.now.code, w.now.day);
      const min = Math.min(...w.days.map(d => d.lo)), max = Math.max(...w.days.map(d => d.hi));
      const span = Math.max(1, max - min);
      const days = w.days.map((d, k) => {
        const c = wCode(d.code, true);
        const left = (d.lo - min) / span * 100, width = (d.hi - d.lo) / span * 100;
        const dot = k === 0 ? `<em style="left:${(clamp(w.now.t, d.lo, d.hi) - min) / span * 100}%"></em>` : '';
        return `<div class="w-day"><span>${d.d}</span><span style="color:${c.y ? 'var(--n-yellow)' : '#fff'};display:flex">${ic(c.i)}</span><span class="lo tnum">${deg(d.lo)}</span>
          <span class="w-bar"><i style="left:${left}%;width:${width}%;--a:${tempColor(d.lo)};--b:${tempColor(d.hi)};animation-delay:${k * 50}ms"></i>${dot}</span><span class="hi tnum">${deg(d.hi)}</span></div>`;
      }).join('');
      const hours = w.hours.map(h => {
        const c = wCode(h.code, h.day);
        return `<div><span>${h.h}</span><i style="color:${c.y ? 'var(--n-yellow)' : '#fff'}">${ic(c.i)}</i><b class="tnum">${deg(h.t)}</b></div>`;
      }).join('');
      return `<div class="w-now" style="--sky:${sky(w.now.code, w.now.day)}">
          <div class="w-head">
            <div>
              <div class="w-place">${p.here ? ic('navigation') : ''}${esc(p.n)}
                <span class="w-nav"><button class="n-icon" type="button" data-act="wPrev" aria-label="Previous place">${ic('chevron-left')}</button><button class="n-icon" type="button" data-act="wNext" aria-label="Next place">${ic('chevron-right')}</button></span></div>
              <div class="w-cond"><span style="color:${cur.y ? 'var(--n-yellow)' : '#fff'};display:flex">${ic(cur.i)}</span>${cur.l}</div>
            </div>
            <div><button class="w-temp tnum" type="button" data-act="wUnit" title="Switch °C and °F">${deg(w.now.t)}</button><div class="w-hl tnum">H:${deg(w.days[0].hi)}  L:${deg(w.days[0].lo)}</div></div>
          </div>
          <div class="w-hours">${hours}</div>
        </div>
        <div class="n-card w-days"><div class="n-cap">7-day forecast</div>${days}
          <div class="w-src">${w.live ? 'Live from Open-Meteo' : 'Sample forecast (live data from Open-Meteo when it can be reached)'}</div></div>`;
    },
    mount() { loadWeather(W.i); }
  };

  // ======================================================================
  //  SPORTS
  // ======================================================================
  // Made-up teams with initials for logos: the app shows the real ones, from ESPN.
  const TEAMS = {
    SAL: { n: 'Salsa City Peppers', s: 'Peppers', c: '#ff453a', r: '14-5-2' },
    GUA: { n: 'Guac Valley Avocados', s: 'Avocados', c: '#30d158', r: '12-6-3' },
    QUE: { n: 'Queso Bay Melts', s: 'Melts', c: '#ffd60a', r: '11-8-1' },
    JAL: { n: 'Jalapeño Heights', s: 'Heights', c: '#63e6a0', r: '9-9-2' },
    CHP: { n: 'Chipotle Smoke', s: 'Smoke', c: '#ff9f0a', r: '8-10-3' },
    TOR: { n: 'Tortilla Flats FC', s: 'Flats', c: '#e8c07a', r: '7-2-1' },
    PIC: { n: 'Pico Harbour', s: 'Harbour', c: '#64d2ff', r: '6-3-1' },
    CRE: { n: 'Crema Rovers', s: 'Rovers', c: '#bf5af2', r: '5-4-1' },
    NAC: { n: 'Nacho Libre Luchadores', s: 'Luchadores', c: '#ff375f', r: '15-4' },
    CHU: { n: 'Churro Town Twisters', s: 'Twisters', c: '#ff9f0a', r: '12-6' }
  };
  const SP = { filter: 'mine', page: 'scores', game: null, mine: ['SAL', 'TOR'] };
  const SP_GAMES = [
    { id: 1, lg: 'hockey', a: 'GUA', h: 'SAL', as: 2, hs: 3, st: 'live', clock: '3rd 6:41', tv: 'Nacho TV', per: [[1, 1, 0], [0, 2, 1]],
      stats: [['Shots', 24, 31], ['Power Play %', 33.3, 50], ['Faceoff %', 46.2, 53.8], ['Hits', 21, 17], ['Blocked Shots', 9, 14]],
      lead: [['Goals', 'M. Verde 2', 'R. Rojo 1'], ['Assists', 'A. Lima 1', 'D. Fuego 2']] },
    { id: 2, lg: 'hockey', a: 'QUE', h: 'JAL', as: 4, hs: 1, st: 'final', clock: 'Final', per: [[2, 1, 1], [0, 1, 0]],
      stats: [['Shots', 35, 22], ['Power Play %', 50, 0], ['Faceoff %', 55, 45], ['Hits', 18, 25]],
      lead: [['Goals', 'Q. Blanco 2', 'J. Picante 1']] },
    { id: 3, lg: 'hockey', a: 'CHP', h: 'GUA', st: 'pre', time: '7:00 PM', tv: 'Fixins+' },
    { id: 4, lg: 'soccer', a: 'PIC', h: 'TOR', as: 1, hs: 1, st: 'live', clock: "67'", tv: 'Queso Sports', per: [[1, 0], [0, 1]],
      stats: [['Possession %', 44, 56], ['Shots', 9, 14], ['Shots on Target', 3, 6], ['Corners', 4, 7], ['Fouls', 11, 8]],
      lead: [['Goals', 'P. Mar 1', 'T. Maiz 1']] },
    { id: 5, lg: 'soccer', a: 'CRE', h: 'PIC', st: 'pre', time: '2:30 PM', tv: 'Queso Sports' },
    { id: 6, lg: 'hoops', a: 'CHU', h: 'NAC', as: 98, hs: 104, st: 'final', clock: 'Final/OT', per: [[24, 22, 27, 21, 4], [20, 30, 22, 22, 10]],
      stats: [['Field Goal %', 44, 49], ['3-Point %', 31, 38], ['Rebounds', 41, 47], ['Assists', 22, 27], ['Turnovers', 15, 11]],
      lead: [['Points', 'C. Azucar 31', 'N. Queso 28'], ['Rebounds', 'C. Canela 12', 'N. Libre 14']] }
  ];
  const SP_LEAGUES = [['hockey', 'Hockey'], ['soccer', 'Soccer'], ['hoops', 'Hoops']];
  const SP_TABLES = {
    hockey: { cols: ['GP', 'W', 'L', 'OTL', 'PTS'], rows: [['SAL', 21, 14, 5, 2, 30], ['GUA', 21, 12, 6, 3, 27], ['QUE', 20, 11, 8, 1, 23], ['JAL', 20, 9, 9, 2, 20], ['CHP', 21, 8, 10, 3, 19]] },
    soccer: { cols: ['GP', 'W', 'D', 'L', 'PTS'], rows: [['TOR', 10, 7, 1, 2, 22], ['PIC', 10, 6, 1, 3, 19], ['CRE', 10, 5, 1, 4, 16]] },
    hoops: { cols: ['W', 'L', 'PCT', 'STRK'], rows: [['NAC', 15, 4, '.789', 'W3'], ['CHU', 12, 6, '.667', 'L1']] }
  };
  const spLogo = (k, size = 26) => `<span class="sp-logo" style="--c:${TEAMS[k].c};--z:${size}px">${k[0]}</span>`;
  // The state of play: a red clock while it's on, the final, or the start time and the network.
  const spMid = g => g.st === 'live' ? `<span class="sp-live"><i></i>${esc(g.clock)}</span><small>${esc(g.tv || '')}</small>`
    : g.st === 'final' ? `<span class="n-sec">${esc(g.clock)}</span>` : `<span>${esc(g.time)}</span><small>${esc(g.tv || '')}</small>`;
  function spCard(g) {
    const pre = g.st === 'pre', fin = g.st === 'final';
    const score = (mine, other, k) => pre ? `<span class="sp-rec">${TEAMS[k].r}</span>` : `<b class="tnum${fin && mine < other ? ' lost' : ''}">${mine}</b>`;
    const mid = spMid(g);
    return `<button class="sp-card" type="button" data-act="spGame" data-id="${g.id}">
      <span class="sp-team">${spLogo(g.a)}<small>${g.a}</small></span>${score(g.as, g.hs, g.a)}
      <span class="sp-mid">${mid}</span>${score(g.hs, g.as, g.h)}<span class="sp-team">${spLogo(g.h)}<small>${g.h}</small></span></button>`;
  }
  function spGamePage(g) {
    const pre = g.st === 'pre';
    const side = k => `<span class="sp-side">${spLogo(k, 44)}<b>${TEAMS[k].s}</b><small>${TEAMS[k].r}</small></span>`;
    const big = (m, o) => `<b class="sp-big tnum${g.st === 'final' && m < o ? ' lost' : ''}">${pre ? '' : m}</b>`;
    let html = `<div class="sp-head" style="--c:${TEAMS[g.h].c}">${side(g.a)}${big(g.as, g.hs)}<span class="sp-mid">${spMid(g)}</span>${big(g.hs, g.as)}${side(g.h)}</div>`;
    if (pre) return html + `<div class="n-card n-sec">Team stats show here once it starts.</div>`;
    const labels = g.lg === 'soccer' ? ['1H', '2H'] : g.per[0].map((_, i) => g.lg === 'hockey' && i > 2 ? 'OT' : g.lg === 'hoops' && i > 3 ? 'OT' : i + 1);
    const line = (k, p, t) => `<span>${k}</span>${p.map(v => `<span class="n-sec">${v}</span>`).join('')}<b>${t}</b>`;
    html += `<div class="n-card sp-line" style="--n:${labels.length}"><span></span>${labels.map(l => `<span class="n-ter">${l}</span>`).join('')}<span class="n-ter">T</span>${line(g.a, g.per[0], g.as)}${line(g.h, g.per[1], g.hs)}</div>`;
    const bars = g.stats.map(([l, a, h]) => {
      const sh = a / (a + h) * 100;
      return `<div class="sp-stat"><b class="${a >= h ? '' : 'n-sec'}">${a}</b><span class="n-sec">${l}</span><b class="${h >= a ? '' : 'n-sec'}">${h}</b>
        <i style="--s:${sh}%;--a:${TEAMS[g.a].c};--h:${TEAMS[g.h].c};--ao:${a >= h ? 1 : .45};--ho:${h >= a ? 1 : .45}"></i></div>`;
    }).join('');
    const leads = g.lead.map(([c, a, h]) => `<div class="sp-lead"><b>${a}</b><span class="n-sec">${c}</span><b>${h}</b></div>`).join('');
    return html + `<div class="n-card stack"><div class="sp-stats-head">${spLogo(g.a, 18)}<span class="n-cap">Team stats</span>${spLogo(g.h, 18)}</div>${bars}</div>
      <div class="n-card stack"><div class="n-cap" style="text-align:center">Leaders</div>${leads}</div>`;
  }
  const sportsView = {
    html() {
      if (SP.page === 'game') {
        const g = SP_GAMES.find(x => x.id === SP.game);
        return `<div class="stack"><div class="row"><button class="n-icon" type="button" data-act="spBack" aria-label="Back">${ic('chevron-left')}</button><b>${TEAMS[g.a].s} at ${TEAMS[g.h].s}</b></div>${spGamePage(g)}</div>`;
      }
      const chip = (id, label, icon) => `<button class="sp-chip${SP.filter === id ? ' on' : ''}" type="button" data-act="spFilter" data-id="${id}">${icon ? ic(icon) : ''}${label}</button>`;
      const head = `<div class="sp-top"><div class="row" style="gap:6px">${chip('mine', 'My Teams', 'star')}${SP_LEAGUES.map(([id, l]) => chip(id, l)).join('')}</div>
        <span class="sp-seg"><button class="n-icon${SP.page === 'scores' ? ' on' : ''}" type="button" data-act="spPage" data-id="scores" aria-label="Scores">${ic('trophy')}</button><button class="n-icon${SP.page === 'standings' ? ' on' : ''}" type="button" data-act="spPage" data-id="standings" aria-label="Standings">${ic('list-ordered')}</button></span></div>`;
      if (SP.page === 'standings') {
        const lg = SP.filter === 'mine' ? 'hockey' : SP.filter;
        const t = SP_TABLES[lg];
        const rows = t.rows.map(([k, ...v], i) => `<div class="sp-row${SP.mine.includes(k) ? ' fav' : ''}" style="--n:${t.cols.length}"><span class="n-ter">${i + 1}</span>${spLogo(k, 16)}<span class="ell">${TEAMS[k].s}</span>${v.map(x => `<span>${x}</span>`).join('')}</div>`).join('');
        return `<div class="stack">${head}<div class="n-card"><div class="sp-row n-cap" style="--n:${t.cols.length}"><span></span><span></span><span>${SP_LEAGUES.find(l => l[0] === lg)[1]}</span>${t.cols.map(c => `<span>${c}</span>`).join('')}</div>${rows}</div></div>`;
      }
      const games = SP_GAMES.filter(g => SP.filter === 'mine' ? SP.mine.includes(g.a) || SP.mine.includes(g.h) : g.lg === SP.filter);
      const today = new Date().toLocaleDateString([], { month: 'short', day: 'numeric' });
      return `<div class="stack">${head}<div class="sp-day"><button class="n-icon" type="button" data-act="spDay" aria-label="Previous day">${ic('chevron-left')}</button><b>Today <span class="n-ter">${today}</span></b><button class="n-icon" type="button" data-act="spDay" aria-label="Next day">${ic('chevron-right')}</button></div>
        <div class="sp-grid">${games.map(spCard).join('')}</div></div>`;
    }
  };

  // ======================================================================
  //  NEWS + RSS
  //  Made-up networks and headlines: the real tab reads the networks you pick (CBC, BBC, Fox News…).
  // ======================================================================
  const min = n => Date.now() - n * 60e3;
  const NW_SOURCES = { nacho: ['Nacho Daily', '#ff453a'], salsa: ['Salsa Times', '#64d2ff'], queso: ['Queso Wire', '#ffd60a'] };
  const NW_STORIES = {
    top: [
      ['queso', 'City council votes to make Taco Tuesday a civic holiday', 'The motion passed 9 to 2 after a lengthy debate over whether Wednesday deserved a turn.', 4],
      ['nacho', 'Record crowds line up for the first guacamole of the season', 'Growers say the avocado harvest is the best in a decade, and the lines agree.', 18],
      ['salsa', 'Hot sauce shortage eases as new peppers reach shelves', 'Restaurants that rationed their bottles all summer can finally stop counting drops.', 41],
      ['queso', 'Local band plays a three-hour set entirely in kazoo', 'Fans called it brave. Neighbours called it something else.', 66],
      ['nacho', 'Study finds snacks taste better when shared, surprising nobody', 'Researchers say the effect doubles when the snack is nachos.', 130]
    ],
    world: [
      ['salsa', 'Summit agrees on a standard size for tortilla chips', 'Delegates spent two days on the question of the perfect scoop.', 9],
      ['nacho', 'Festival of chillies draws visitors from forty countries', 'The hottest pepper on show was handled with tongs and great respect.', 37],
      ['queso', 'Cheese exports climb for the fifth straight month', 'Analysts credit a global craving for anything that melts.', 92]
    ],
    tech: [
      ['salsa', 'New laptops put a live panel around the camera', 'The notch, once a nuisance, is quietly becoming the busiest pixels on the screen.', 12],
      ['queso', 'Phone makers race to put more cheese in the camera bump', 'Not literally. Probably.', 75]
    ]
  };
  const NW = { filter: 'top' };
  const RSS_FEEDS = [['chips', 'The Chip Blog'], ['dip', 'Dip of the Day']];
  const RSS_STORIES = [
    ['dip', 'Seven-layer dip, ranked layer by layer', 'Beans make a strong showing. Olives remain divisive.', 25],
    ['chips', 'Why the corner chip is always the best chip', 'More crunch, more seasoning, more joy. A short essay.', 180],
    ['dip', 'Is queso a dip or a lifestyle?', 'We asked around. Nobody wanted to answer.', 300],
    ['chips', 'Baked or fried: an honest taste test', 'Spoiler: we ate both bowls before taking notes.', 1440]
  ];
  const RS = { filter: 'all' };
  function feedRow(source, color, t, summary, minsAgo) {
    return `<div class="list-row nw-row"><div class="grow"><div class="nw-meta"><b style="color:${color}">${esc(source)}</b><span>${ago(min(minsAgo))} ago</span></div>
      <div class="nw-title">${esc(t)}</div><div class="nw-sum ell">${esc(summary)}</div></div></div>`;
  }
  const feedChip = (act, cur, id, label) => `<button class="sp-chip${cur === id ? ' on' : ''}" type="button" data-act="${act}" data-id="${id}">${label}</button>`;
  const feedFoot = '<p class="n-ter" style="margin:8px 2px 0;font-size:12px">In the app, a click opens the story in your browser.</p>';
  const newsView = {
    html() {
      const chips = [['top', 'Top Stories'], ['world', 'World'], ['tech', 'Technology']].map(([id, l]) => feedChip('nwFilter', NW.filter, id, l)).join('');
      const rows = NW_STORIES[NW.filter].map(([src, t, sum, m]) => feedRow(NW_SOURCES[src][0], NW_SOURCES[src][1], t, sum, m)).join('');
      return `<div class="row" style="gap:6px;margin-bottom:10px">${chips}</div><div class="n-card nw-list">${rows}</div>${feedFoot}`;
    }
  };
  const rssView = {
    html() {
      const chips = [['all', 'All'], ...RSS_FEEDS].map(([id, l]) => feedChip('rsFilter', RS.filter, id, l)).join('');
      const rows = RSS_STORIES.filter(([f]) => RS.filter === 'all' || RS.filter === f)
        .map(([f, t, sum, m]) => feedRow(RSS_FEEDS.find(x => x[0] === f)[1], '#ff9f0a', t, sum, m)).join('');
      return `<div class="row" style="gap:6px;margin-bottom:10px">${chips}</div><div class="n-card nw-list">${rows}</div>${feedFoot}`;
    }
  };

  // ======================================================================
  //  FILE TRAY
  // ======================================================================
  const DESK = [
    { id: 'd1', name: 'Q4 report.pdf', ext: 'PDF', c: '#ff453a' },
    { id: 'd2', name: 'Lake photo.jpg', ext: 'JPG', c: '#0a84ff', thumb: 'linear-gradient(170deg,#fcd34d,#f97316 40%,#1e3a8a 41%,#0c4a6e)' },
    { id: 'd3', name: 'Keynote.key', ext: 'KEY', c: '#ff9f0a' },
    { id: 'd4', name: 'Assets.zip', ext: 'ZIP', c: '#8e8e93' }
  ];
  const T = { files: [{ id: uid(), name: 'Release notes.txt', ext: 'TXT', c: '#64d2ff' }] };
  function middleEllipsis(s, n = 14) { return s.length <= n ? s : s.slice(0, 6) + '…' + s.slice(-6); }
  function addToTray(f, quiet) {
    T.files.push({ id: uid(), ...f });
    if (!quiet) toast(`Added “${f.name}” to the tray`, { icon: 'inbox' });
    if (S.mode === 'panel' && S.tab === 'tray') refresh();
  }
  function restoreDesk(f) {
    if (!f.origin) return;
    const el = $(`.dfile[data-id="${f.origin}"]`);
    if (el) el.classList.remove('gone');
  }
  const trayView = {
    html() {
      const tiles = T.files.map(f => `<div class="tile" title="${esc(f.name)}">
          ${f.thumb ? `<span class="fthumb" style="background:${f.thumb.startsWith('data:') || f.thumb.startsWith('blob:') ? `url(${f.thumb})` : f.thumb};background-size:cover"></span>` : `<span class="ftype" style="box-shadow:inset 0 -12px 0 -2px ${f.c}">${esc(f.ext)}</span>`}
          <span class="fname">${esc(middleEllipsis(f.name))}</span>
          <span class="facts"><button class="n-icon" type="button" data-act="tCopy" data-id="${f.id}" aria-label="Copy">${ic('copy')}</button><button class="n-icon" type="button" data-act="tReveal" data-id="${f.id}" aria-label="Reveal">${ic('search')}</button><button class="n-icon" type="button" data-act="tRemove" data-id="${f.id}" aria-label="Remove">${ic('x')}</button></span>
        </div>`).join('');
      const body = T.files.length ? `<div class="tray-grid">${tiles}</div>` :
        `<div class="drop-zone"><div>${ic('arrow-down-to-line')}<div>Drop files here to shelve them</div><div class="n-ter" style="font-size:11px;margin-top:4px">Try one from the desktop below, or from your own computer</div></div></div>`;
      return `${body}<div class="tray-foot"><span class="n-sec grow">${T.files.length} file${T.files.length === 1 ? '' : 's'}</span>
        <button class="n-btn white" type="button" data-act="tCopyAll" ${T.files.length ? '' : 'disabled'}>${ic('copy')}Copy</button>
        <button class="n-btn" type="button" data-act="tAirDrop" ${T.files.length ? '' : 'disabled'}>${ic('radio')}AirDrop</button>
        <button class="n-btn" type="button" data-act="tRevealAll" ${T.files.length ? '' : 'disabled'}>${ic('folder')}Reveal</button>
        <button class="n-btn red" type="button" data-act="tClear" ${T.files.length ? '' : 'disabled'}>${ic('trash-2')}Clear</button></div>`;
    }
  };

  // ======================================================================
  //  SYSTEM
  // ======================================================================
  const SY = { cpu: 9, gpu: 21, mem: 47, hist: Array.from({ length: 24 }, () => rnd(5, 14)), down: 8, up: 4, dr: 0, dw: 56, t: 0 };
  const memTotal = 16;
  function sysStep() {
    SY.cpu = clamp(SY.cpu + rnd(-6, 6) + (Math.random() < .1 ? rnd(10, 30) : 0) - (SY.cpu > 40 ? 8 : 0), 2, 96);
    SY.gpu = clamp(SY.gpu + rnd(-5, 5), 3, 90);
    SY.mem = clamp(SY.mem + rnd(-.6, .6), 35, 80);
    SY.hist.push(SY.cpu); SY.hist.shift();
    SY.down = Math.max(0, SY.down * rnd(.4, 1.6) + rnd(0, 40) * (Math.random() < .3 ? 1 : 0));
    SY.up = Math.max(0, SY.up * rnd(.4, 1.5) + rnd(0, 10) * (Math.random() < .3 ? 1 : 0));
    SY.dr = Math.random() < .25 ? rnd(0, 3000) : 0;
    SY.dw = Math.random() < .5 ? rnd(4, 400) : 0;
  }
  const rate = kb => kb >= 1024 ? (kb / 1024).toFixed(1) + ' MB/s' : kb >= 1 ? kb.toFixed(1) + ' KB/s' : '0 B/s';
  const RC = 2 * Math.PI * 23;
  function ring(id, label, color, sub) {
    return `<div class="n-card st-ring"><svg viewBox="0 0 54 54"><circle class="bg" cx="27" cy="27" r="23"/><circle class="fg" id="r_${id}" cx="27" cy="27" r="23" stroke="${color}" stroke-dasharray="${RC}" stroke-dashoffset="${RC}"/></svg>
      <div><div class="lbl" style="color:${color}">${label}</div><div class="val" id="v_${id}">0%</div><div class="sub" id="s_${id}">${sub}</div></div></div>`;
  }
  function drawChart() {
    const cv = $('#stChart');
    if (!cv) return;
    const dpr = devicePixelRatio || 1, w = cv.clientWidth, h = cv.clientHeight;
    cv.width = w * dpr; cv.height = h * dpr;
    const g = cv.getContext('2d');
    g.scale(dpr, dpr);
    g.strokeStyle = 'rgba(255,255,255,.06)'; g.lineWidth = 1;
    [0.25, 0.5, 0.75].forEach(f => { g.beginPath(); g.moveTo(0, h * f); g.lineTo(w, h * f); g.stroke(); });
    const pts = SY.hist.map((v, i) => [i / (SY.hist.length - 1) * (w - 6) + 3, h - 3 - v / 100 * (h - 8)]);
    const grad = g.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, 'rgba(100,210,255,.35)'); grad.addColorStop(1, 'rgba(100,210,255,0)');
    g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y));
    g.lineTo(pts[pts.length - 1][0], h); g.lineTo(pts[0][0], h); g.closePath(); g.fillStyle = grad; g.fill();
    g.beginPath(); pts.forEach(([x, y], i) => i ? g.lineTo(x, y) : g.moveTo(x, y));
    g.strokeStyle = '#64d2ff'; g.lineWidth = 2; g.lineJoin = 'round'; g.stroke();
    const [lx, ly] = pts[pts.length - 1];
    g.beginPath(); g.arc(lx, ly, 3.5, 0, Math.PI * 2); g.fillStyle = '#fff'; g.fill();
  }
  function sysPaint() {
    [['cpu', SY.cpu], ['gpu', SY.gpu], ['mem', SY.mem]].forEach(([id, v]) => {
      const r = $('#r_' + id);
      if (!r) return;
      r.style.strokeDashoffset = RC * (1 - v / 100);
      $('#v_' + id).textContent = Math.round(v) + '%';
    });
    const sm = $('#s_mem');
    if (!sm) return;
    sm.textContent = `${(SY.mem / 100 * memTotal).toFixed(1)} GB of ${memTotal}.0 GB`;
    $('#stCpu').textContent = Math.round(SY.cpu) + '%';
    $('#ioDown').textContent = rate(SY.down); $('#ioUp').textContent = rate(SY.up);
    $('#ioR').textContent = rate(SY.dr); $('#ioW').textContent = rate(SY.dw);
    drawChart();
  }
  const statsView = {
    html() {
      return `<div class="st-rings">${ring('cpu', 'CPU', '#64D2FF', 'in use')}${ring('gpu', 'GPU', '#BF5AF2', 'in use')}${ring('mem', 'MEMORY', '#30D158', '')}</div>
        <div class="n-card st-chart"><div class="row"><span class="n-cap grow">CPU · last 24 seconds</span><b id="stCpu" style="color:#64D2FF" class="tnum"></b></div><canvas id="stChart"></canvas></div>
        <div class="st-io">
          <div class="n-card"><div class="n-cap">Network</div><div class="vals"><span><i class="dotico" style="background:#1d3a6e;color:#0a84ff">${ic('arrow-down')}</i><b id="ioDown"></b></span><span><i class="dotico" style="background:#5a1f2a;color:#ff453a">${ic('arrow-up')}</i><b id="ioUp"></b></span></div></div>
          <div class="n-card"><div class="n-cap">Disk</div><div class="vals"><span><i class="dotico" style="background:#4a3412;color:#ff9f0a">${ic('arrow-down')}</i><b id="ioR"></b></span><span><i class="dotico" style="background:#4a4512;color:#ffd60a">${ic('arrow-up')}</i><b id="ioW"></b></span></div></div>
        </div>`;
    },
    mount() { requestAnimationFrame(sysPaint); this.iv = setInterval(() => { sysStep(); sysPaint(); }, 1000); },
    unmount() { clearInterval(this.iv); }
  };

  // ======================================================================
  //  PROCESSES
  // ======================================================================
  const PROCS = [
    ['Safari', '#0a84ff', 14, 1840, 120, 340], ['Xcode', '#64d2ff', 28, 3120, 880, 12], ['Music', '#ff375f', 3, 420, 40, 220],
    ['Slack', '#bf5af2', 6, 980, 30, 64], ['Figma', '#ff9f0a', 11, 1460, 22, 48], ['Terminal', '#8e8e93', 1, 96, 2, 0],
    ['Finder', '#64d2ff', 1, 210, 10, 0], ['Code', '#0a84ff', 9, 1210, 60, 8], ['Docker', '#0a84ff', 12, 2280, 420, 96],
    ['Notcho', '#ffc93c', 1, 148, 1, 3], ['Mail', '#0a84ff', 2, 320, 14, 40], ['Notes', '#ffd60a', 1, 180, 4, 2]
  ].map(([n, c, cpu, mem, disk, net], id) => ({ id, n, c, cpu, mem, disk, net, b: { cpu, mem, disk, net } }));
  const P = { sort: 'cpu', desc: true, ask: -1, askT: 0 };
  const mem = mb => mb >= 1024 ? (mb / 1024).toFixed(1) + ' GB' : Math.round(mb) + ' MB';
  function procStep() {
    PROCS.forEach(p => {
      p.cpu = Math.max(0, p.b.cpu * rnd(.4, 1.8));
      p.mem = p.b.mem * rnd(.97, 1.04);
      p.disk = Math.random() < .5 ? p.b.disk * rnd(0, 2) : 0;
      p.net = Math.random() < .6 ? p.b.net * rnd(0, 2) : 0;
    });
  }
  function procRows() {
    const k = P.sort, dir = P.desc ? -1 : 1;
    const rows = PROCS.slice().sort((a, b) => (k === 'n' ? a.n.localeCompare(b.n) : a[k] - b[k]) * dir).slice(0, 10);
    return rows.map(p => `<tr data-pid="${p.id}"><td><span class="pdot" style="background:${p.c}"></span>${esc(p.n)}</td><td>${p.cpu.toFixed(1)}%</td><td>${mem(p.mem)}</td><td>${rate(p.disk)}</td><td>${rate(p.net)}</td>
      <td style="width:28px"><button class="n-icon kill${P.ask === p.id ? ' ask' : ''}" type="button" data-act="pKill" data-id="${p.id}" aria-label="Force quit ${esc(p.n)}">${P.ask === p.id ? 'Quit?' : ic('x')}</button></td></tr>`).join('');
  }
  const procView = {
    html() {
      const th = (k, l) => `<th class="${P.sort === k ? 'on' : ''}" data-act="pSort" data-k="${k}">${l}${P.sort === k ? (P.desc ? ' ↓' : ' ↑') : ''}</th>`;
      return `<div class="n-card"><table class="procs"><thead><tr>${th('n', 'Name')}${th('cpu', 'CPU')}${th('mem', 'Memory')}${th('disk', 'Disk')}${th('net', 'Network')}<th></th></tr></thead><tbody id="pBody">${procRows()}</tbody></table></div>
        <p class="n-ter" style="margin:8px 2px 0;font-size:11px">Your own processes only. Click a heading to sort, again to reverse. The cross asks first, then force quits.</p>`;
    },
    mount() { this.iv = setInterval(() => { procStep(); const b = $('#pBody'); if (b) b.innerHTML = procRows(); }, 1000); },
    unmount() { clearInterval(this.iv); }
  };

  // ======================================================================
  //  CAPTURE (macshot shortcuts)
  // ======================================================================
  const C = { recording: false, recStart: 0, recent: [] };
  function captureThumb(label, rect) {
    const cv = document.createElement('canvas');
    cv.width = 320; cv.height = 200;
    const g = cv.getContext('2d');
    const gr = g.createLinearGradient(0, 0, 320, 200);
    gr.addColorStop(0, '#2e1065'); gr.addColorStop(.55, '#0e7490'); gr.addColorStop(1, '#a21caf');
    g.fillStyle = gr; g.fillRect(0, 0, 320, 200);
    g.fillStyle = 'rgba(0,0,0,.55)'; g.fillRect(0, 0, 320, 12);
    g.fillStyle = '#000'; g.beginPath(); g.roundRect ? g.roundRect(125, 0, 70, 12, [0, 0, 5, 5]) : g.rect(125, 0, 70, 12); g.fill();
    if (rect) {
      const sx = 320 / innerWidth, sy = 200 / innerHeight;
      g.strokeStyle = '#fff'; g.lineWidth = 2; g.strokeRect(rect.x * sx, rect.y * sy, rect.w * sx, rect.h * sy);
    }
    g.fillStyle = '#fff'; g.font = '600 18px -apple-system, Geist, sans-serif'; g.fillText(label, 16, 186);
    return cv.toDataURL('image/png');
  }
  function foldAway() { S.sticky = false; setMode('peek'); }
  function snapSelect(onDone) {
    const snap = $('#snap'), r = $('#snapRect');
    snap.hidden = false; r.style.display = 'none';
    let start = null, rect = null;
    const finish = (ok) => {
      snap.hidden = true;
      snap.onpointerdown = snap.onpointermove = snap.onpointerup = null;
      document.removeEventListener('keydown', onKey);
      if (ok && rect && rect.w > 6 && rect.h > 6) onDone(rect);
    };
    const onKey = e => { if (e.key === 'Escape') finish(false); };
    document.addEventListener('keydown', onKey);
    snap.onpointerdown = e => { start = { x: e.clientX, y: e.clientY }; snap.setPointerCapture(e.pointerId); };
    snap.onpointermove = e => {
      if (!start) return;
      rect = { x: Math.min(start.x, e.clientX), y: Math.min(start.y, e.clientY), w: Math.abs(e.clientX - start.x), h: Math.abs(e.clientY - start.y) };
      Object.assign(r.style, { display: 'block', left: rect.x + 'px', top: rect.y + 'px', width: rect.w + 'px', height: rect.h + 'px' });
    };
    snap.onpointerup = () => finish(true);
  }
  function flash() { const f = $('#flash'); f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); }
  function textInRect(rect) {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const nodes = [];
    let n = walker.nextNode();
    while (n) { nodes.push(n); n = walker.nextNode(); }
    const range = document.createRange();
    return nodes
      .filter(t => t.textContent.trim() && !t.parentElement.closest('#notchWrap, #snap, script, style, .toasts'))
      .filter(t => {
        range.selectNodeContents(t);
        const b = range.getBoundingClientRect();
        return b.width > 0 && b.right > rect.x && b.left < rect.x + rect.w && b.bottom > rect.y && b.top < rect.y + rect.h;
      })
      .map(t => t.textContent.trim().replace(/\s+/g, ' '))
      .join(' ');
  }
  function saveCapture(name, rect) {
    const thumb = captureThumb(name, rect);
    C.recent.unshift(thumb); C.recent = C.recent.slice(0, 3);
    addToTray({ name: name + '.png', ext: 'PNG', c: '#0a84ff', thumb }, true);
    toast(`${name} saved and added to the tray`, { icon: 'camera' });
  }
  function capture(kind) {
    const t = new Date();
    if (kind === 'record') {
      if (C.recording) {
        C.recording = false;
        addToTray({ name: `Recording ${stamp(t)}.mov`, ext: 'MOV', c: '#ff453a' }, true);
        toast(`Recording saved (${mss((Date.now() - C.recStart) / 1000)}) and added to the tray`, { icon: 'video' });
        renderPeek();
        if (S.mode === 'panel') refresh();
        return;
      }
      C.recording = true; C.recStart = Date.now();
      foldAway();
      toast('Recording the screen. Open Capture again to stop.', { icon: 'video' });
      return;
    }
    foldAway();
    setTimeout(() => {
      if (kind === 'screen' || kind === 'scroll') { flash(); saveCapture(kind === 'screen' ? `Screen ${stamp(t)}` : `Scrolling ${stamp(t)}`); return; }
      snapSelect(rect => {
        flash();
        if (kind === 'text') {
          const text = textInRect(rect).slice(0, 2000);
          if (!text) { toast('No text found in that area', { icon: 'scan-text' }); return; }
          copyText(text);
          addClip(text);
          toast(`Copied ${text.split(' ').length} words of recognised text`, { icon: 'scan-text' });
          return;
        }
        saveCapture(`Area ${stamp(t)}`, rect);
      });
    }, 450);
  }
  const captureView = {
    html() {
      const b = (k, i, l) => `<button class="cap${k === 'record' && C.recording ? ' live' : ''}" type="button" data-act="cap" data-k="${k}">${ic(i)}${k === 'record' && C.recording ? 'Stop' : l}</button>`;
      const recent = C.recent.length ? C.recent.map(u => `<span style="background-image:url(${u})"></span>`).join('') :
        '<p class="n-ter" style="margin:6px 2px;font-size:12px">Your latest captures show here, to open in macshot\'s editor.</p>';
      return `<div class="cap-grid">${b('area', 'crop', 'Area')}${b('screen', 'monitor', 'Screen')}${b('scroll', 'scroll-text', 'Scrolling')}${b('text', 'scan-text', 'Text')}${b('record', 'video', 'Record')}</div>
        <div class="n-card" style="margin-top:10px"><div class="n-cap">Latest captures</div><div class="cap-recent">${recent}</div></div>
        <p class="n-ter" style="margin:8px 2px 0;font-size:11px">Shortcuts into macshot, the open-source screenshot app. The notch folds away first, so it isn't in the shot.</p>`;
    }
  };

  // ======================================================================
  //  MIRROR
  // ======================================================================
  const LOOKS = [
    ['None', ''], ['Mono', 'grayscale(1)'], ['Noir', 'grayscale(1) contrast(1.7) brightness(.9)'], ['Sepia', 'sepia(.85)'],
    ['Vivid', 'saturate(1.9) contrast(1.1)'], ['Chrome', 'saturate(1.35) contrast(1.15) hue-rotate(-8deg)'], ['Fade', 'contrast(.8) brightness(1.08) saturate(.65)'], ['Thermal', 'invert(1) hue-rotate(180deg) saturate(3)']
  ];
  const MR = { look: 0, bright: 50, stream: null, raf: 0, t0: performance.now() };
  const mirrorFilter = () => `${LOOKS[MR.look][1]} brightness(${(.5 + MR.bright / 100).toFixed(2)})`.trim();
  function standIn(cv) {
    const g = cv.getContext('2d');
    const w = cv.width, h = cv.height;
    const t = (performance.now() - MR.t0) / 1000;
    const bg = g.createLinearGradient(0, 0, w, h);
    bg.addColorStop(0, '#5b5446'); bg.addColorStop(.6, '#a9a48f'); bg.addColorStop(1, '#d6dde2');
    g.filter = 'blur(18px)';
    g.fillStyle = bg; g.fillRect(-40, -40, w + 80, h + 80);
    const sway = Math.sin(t * .8) * 10, bob = Math.sin(t * 1.3) * 4;
    g.fillStyle = '#2a2320'; g.beginPath(); g.ellipse(w / 2 + sway * .6, h * 1.02, w * .34, h * .36, 0, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#9a6b58'; g.beginPath(); g.ellipse(w / 2 + sway, h * .48 + bob, w * .12, h * .25, 0, 0, Math.PI * 2); g.fill();
    g.fillStyle = '#3a2a24'; g.beginPath(); g.ellipse(w / 2 + sway, h * .3 + bob, w * .13, h * .12, 0, 0, Math.PI * 2); g.fill();
    g.filter = 'none';
  }
  function mirrorLoop() {
    const cv = $('#mrCanvas');
    if (!cv) return;
    standIn(cv);
    MR.raf = requestAnimationFrame(mirrorLoop);
  }
  function stopCamera() {
    cancelAnimationFrame(MR.raf);
    if (MR.stream) { MR.stream.getTracks().forEach(t => t.stop()); MR.stream = null; }
  }
  async function startCamera() {
    const msg = $('#mrMsg');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { width: 1280, height: 720 } });
      if (S.tab !== 'mirror' || S.mode !== 'panel') { stream.getTracks().forEach(t => t.stop()); return; }
      MR.stream = stream;
      cancelAnimationFrame(MR.raf);
      const box = $('#mrBox');
      box.innerHTML = '<video id="mrVideo" autoplay playsinline muted></video>';
      const v = $('#mrVideo');
      v.srcObject = stream; v.style.filter = mirrorFilter();
    } catch {
      if (msg) msg.innerHTML = '<span>This browser didn\'t allow the camera, so here\'s a stand-in. On your Mac it\'s your camera, flipped like a mirror.</span>';
    }
  }
  function shutter() {
    const src = $('#mrVideo') || $('#mrCanvas');
    if (!src) return;
    const cv = document.createElement('canvas');
    cv.width = 640; cv.height = 360;
    const g = cv.getContext('2d');
    g.filter = mirrorFilter();
    g.translate(640, 0); g.scale(-1, 1);
    g.drawImage(src, 0, 0, 640, 360);
    const url = cv.toDataURL('image/jpeg', .85);
    const box = $('#mrBox');
    box.animate([{ filter: 'brightness(3)' }, { filter: 'none' }], { duration: 400 });
    addToTray({ name: `Mirror ${stamp(new Date())}.jpg`, ext: 'JPG', c: '#bf5af2', thumb: url }, true);
    toast('Saved to Pictures/Notcho and added to the tray', { icon: 'camera' });
  }
  const mirrorView = {
    html() {
      return `<div class="mirror" id="mrBox"><canvas id="mrCanvas" width="480" height="270" style="filter:${mirrorFilter()}"></canvas>
          <div class="m-msg" id="mrMsg"><span>The camera only runs while this tab is open.</span><button class="n-btn white" type="button" data-act="mrCam">${ic('camera')}Turn on camera</button></div></div>
        <div class="row" style="margin-top:10px;color:var(--n-sec)">${ic('sun')}<input class="slider" id="mrBright" type="range" min="0" max="100" value="${MR.bright}" style="--c:#b8a88a;--v:${MR.bright}%" aria-label="Mirror brightness"><span class="n-ter" style="white-space:nowrap">This view only</span></div>
        <div class="mirror-ctl">
          <div class="looks"><button class="n-icon" type="button" data-act="mrLook" data-d="-1" aria-label="Previous look">${ic('chevron-left')}</button><b id="mrLook">${LOOKS[MR.look][0]}</b><button class="n-icon" type="button" data-act="mrLook" data-d="1" aria-label="Next look">${ic('chevron-right')}</button></div>
          <button class="n-btn white shutter" type="button" data-act="mrShot" aria-label="Take a photo">${ic('camera')}</button>
          <span class="n-btn" style="cursor:default">MacBook Pro Camera ${ic('chevrons-up-down')}</span>
        </div>`;
    },
    mount(root) {
      mirrorLoop();
      $('#mrBright', root).addEventListener('input', e => {
        MR.bright = +e.target.value;
        e.target.style.setProperty('--v', MR.bright + '%');
        const target = $('#mrVideo') || $('#mrCanvas');
        target.style.filter = mirrorFilter();
      });
    },
    unmount() { stopCamera(); }
  };

  // ======================================================================
  //  VOICE NOTES
  // ======================================================================
  const MICS = ['MacBook Pro Microphone', 'AirPods Pro', 'Studio Display Microphone'];
  const N = {
    rec: false, start: 0, mic: 0, recorder: null, chunks: [], analyser: null, stream: null, raf: 0,
    notes: [
      { id: uid(), at: Date.now() - 2.3 * 3600e3, dur: 95 },
      { id: uid(), at: Date.now() - 6.8 * 3600e3, dur: 42 }
    ],
    playing: null, audio: null, playStart: 0, playRaf: 0
  };
  function noteWhen(at) { const d = new Date(at); return `${dayLabel(d)}, ${clock(d)}`; }
  function meterLoop() {
    const bar = $('#nMeter');
    let level = rnd(.05, .6);
    if (N.analyser) {
      const buf = new Uint8Array(N.analyser.fftSize);
      N.analyser.getByteTimeDomainData(buf);
      level = Math.min(1, Math.sqrt(buf.reduce((s, v) => s + ((v - 128) / 128) ** 2, 0) / buf.length) * 4);
    }
    if (bar) bar.style.width = (level * 100) + '%';
    const tm = $('#nTime');
    if (tm) tm.textContent = mss((Date.now() - N.start) / 1000);
    N.raf = requestAnimationFrame(meterLoop);
  }
  async function toggleRecord() {
    audioCtx();
    if (N.rec) {
      N.rec = false;
      cancelAnimationFrame(N.raf);
      const dur = (Date.now() - N.start) / 1000;
      const note = { id: uid(), at: Date.now(), dur };
      if (N.recorder) {
        N.recorder.onstop = () => { note.url = URL.createObjectURL(new Blob(N.chunks, { type: N.recorder.mimeType || 'audio/webm' })); N.stream.getTracks().forEach(t => t.stop()); N.recorder = null; N.analyser = null; };
        N.recorder.stop();
      }
      N.notes.unshift(note);
      refresh();
      return;
    }
    N.rec = true; N.start = Date.now(); N.chunks = [];
    refresh();
    meterLoop();
    try {
      N.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!N.rec) { N.stream.getTracks().forEach(t => t.stop()); return; }
      N.recorder = new MediaRecorder(N.stream);
      N.recorder.ondataavailable = e => N.chunks.push(e.data);
      N.recorder.start();
      const ctx = audioCtx();
      const src = ctx.createMediaStreamSource(N.stream);
      N.analyser = ctx.createAnalyser(); N.analyser.fftSize = 512;
      src.connect(N.analyser);
    } catch {
      toast('No microphone here, so this note will be silent', { icon: 'mic-off' });
    }
  }
  function stopPlayback() {
    cancelAnimationFrame(N.playRaf);
    if (N.audio) { N.audio.pause(); N.audio = null; }
    N.playing = null;
  }
  function playNote(id) {
    const wasPlaying = N.playing === id;
    stopPlayback();
    if (wasPlaying) { refresh(); return; }
    const note = N.notes.find(n => n.id === id);
    N.playing = id; N.playStart = performance.now();
    if (note.url) { N.audio = new Audio(note.url); N.audio.volume = S.volume / 100; N.audio.play().catch(() => {}); }
    refresh();
    const step = () => {
      const el = $(`#np_${id}`);
      const p = (performance.now() - N.playStart) / 1000 / note.dur;
      if (el) el.style.width = Math.min(100, p * 100) + '%';
      if (p >= 1) { stopPlayback(); refresh(); return; }
      N.playRaf = requestAnimationFrame(step);
    };
    step();
  }
  const notesView = {
    html() {
      const rows = N.notes.map(n => `<div class="note-row">
          <button class="n-icon" type="button" data-act="nPlay" data-id="${n.id}" aria-label="Play" style="color:#fff">${ic(N.playing === n.id ? 'pause' : 'play')}</button>
          <div class="grow"><div style="font-size:14px">${noteWhen(n.at)}</div><div class="note-prog"><i id="np_${n.id}"></i></div></div>
          <span class="n-sec tnum">${mss(n.dur)}</span>
          <button class="n-icon" type="button" data-act="nTray" data-id="${n.id}" aria-label="Put in tray">${ic('inbox')}</button>
          <button class="n-icon" type="button" data-act="nReveal" aria-label="Reveal">${ic('search')}</button>
          <button class="n-icon" type="button" data-act="nDel" data-id="${n.id}" aria-label="Delete">${ic('trash-2')}</button></div>`).join('');
      return `<div class="n-card rec-card">
          <button class="rec-btn${N.rec ? ' on' : ''}" type="button" data-act="nRec" aria-label="${N.rec ? 'Stop recording' : 'Record'}">${ic(N.rec ? 'square' : 'mic')}</button>
          <div class="grow"><div class="rec-time${N.rec ? ' on' : ''}" id="nTime">${N.rec ? mss((Date.now() - N.start) / 1000) : '0:00'}</div><div class="meter"><i id="nMeter"></i></div></div>
          <button class="n-btn" type="button" data-act="nMic">${ic('mic')}${MICS[N.mic]}</button></div>
        <div class="n-card" style="margin-top:10px;padding-block:6px">${rows || '<div class="empty">No notes yet. Click the microphone to record one.</div>'}</div>`;
    }
  };

  // ======================================================================
  //  TASKS
  // ======================================================================
  const TK = {
    list: store.get('tasks', [
      { id: 'a', t: 'Send the release notes to the team', done: false },
      { id: 'b', t: 'Review the Windows build', done: false },
      { id: 'c', t: 'Record the demo video', done: true, doneAt: Date.now() }
    ]).filter(x => !x.done || Date.now() - (x.doneAt || 0) < 86400e3)
  };
  const saveTasks = () => store.set('tasks', TK.list);
  const tasksView = {
    html() {
      const open = TK.list.filter(x => !x.done).length;
      const rows = TK.list.map(x => `<div class="list-row"><button class="check${x.done ? ' on' : ''}" type="button" data-act="tkCheck" data-id="${x.id}" aria-label="${x.done ? 'Uncheck' : 'Check off'}">${ic('check')}</button>
        <span class="task-t grow${x.done ? ' done' : ''}">${esc(x.t)}</span><button class="n-icon" type="button" data-act="tkDel" data-id="${x.id}" aria-label="Delete">${ic('x')}</button></div>`).join('');
      return `<div class="row" style="margin-bottom:10px"><span class="n-cap grow">${open} to do</span><button class="n-btn blue" type="button" data-act="tkAdd">${ic('plus')}Add</button></div>
        <div class="n-card" style="padding:6px 12px">${rows || '<div class="empty">Nothing to do. Add a task and it waits here.</div>'}</div>
        <p class="n-ter" style="margin:8px 2px 0;font-size:12px">Checked-off tasks clear away after a day.</p>`;
    }
  };

  // ======================================================================
  //  CLIPBOARD
  // ======================================================================
  const CB = {
    list: store.get('clips', [
      { id: 'x1', t: 'https://shinylib.net', pinned: true, at: Date.now() - 3 * 3600e3 },
      { id: 'x2', t: 'dotnet build -c Release', pinned: false, at: Date.now() - 22 * 60e3 },
      { id: 'x3', t: 'Second copy with two lines\nand this is the second line', pinned: false, at: Date.now() - 4 * 60e3 }
    ])
  };
  const saveClips = () => store.set('clips', CB.list);
  function addClip(text) {
    const t = text.trim();
    if (!t) return;
    const existing = CB.list.find(c => c.t === t);
    CB.list = CB.list.filter(c => c !== existing);
    CB.list.unshift({ id: uid(), t, pinned: existing ? existing.pinned : false, at: Date.now() });
    const unpinned = CB.list.filter(c => !c.pinned);
    const drop = new Set(unpinned.slice(50).map(c => c.id));
    CB.list = CB.list.filter(c => !drop.has(c.id));
    saveClips();
    if (S.mode === 'panel' && S.tab === 'clipboard') refresh();
  }
  const clipView = {
    html() {
      const sorted = [...CB.list.filter(c => c.pinned), ...CB.list.filter(c => !c.pinned)];
      const rows = sorted.map(c => `<div class="list-row clip-row" data-act="cbCopy" data-id="${c.id}">
          <span class="clip-t grow ell${c.pinned ? ' pinned' : ''}">${esc(c.t.split('\n')[0])}</span><span class="n-ter" style="font-size:12px">${ago(c.at)}</span>
          <button class="n-icon" type="button" data-act="cbPin" data-id="${c.id}" aria-label="${c.pinned ? 'Unpin' : 'Pin'}" style="${c.pinned ? 'color:var(--n-orange)' : ''}">${ic(c.pinned ? 'pin' : 'pin')}</button>
          <button class="n-icon" type="button" data-act="cbDel" data-id="${c.id}" aria-label="Remove">${ic('x')}</button></div>`).join('');
      return `<div class="row" style="margin-bottom:10px"><span class="n-cap grow">${CB.list.length} copied</span><button class="n-btn" type="button" data-act="cbClear">${ic('trash-2')}Clear</button></div>
        <div class="n-card" style="padding:6px 8px">${rows || '<div class="empty">Copy something and it shows up here.</div>'}</div>
        <p class="n-ter" style="margin:8px 2px 0;font-size:12px">Pick an entry to put it back on the clipboard. Copy text anywhere on this page to add it.</p>`;
    }
  };

  // ======================================================================
  //  CALENDAR
  // ======================================================================
  function seedEvents() {
    const today = dayStart(new Date()), tom = addDays(today, 1);
    const at = (d, h, m) => new Date(d.getFullYear(), d.getMonth(), d.getDate(), h, m).getTime();
    const soon = new Date(Math.ceil((Date.now() + 38 * 60e3) / 300e3) * 300e3).getTime();
    return [
      { id: 's1', t: 'Standup', at: at(today, 9, 30), mins: 15, c: '#0a84ff' },
      { id: 's2', t: 'Design review', at: soon, mins: 45, c: '#ff9f0a' },
      { id: 's3', t: 'Climbing gym', at: at(today, 21, 30), mins: 90, c: '#30d158' },
      { id: 's4', t: 'Dentist', at: at(tom, 10, 0), mins: 60, c: '#bf5af2' },
      { id: 's5', t: 'Ship the 1.4 release', at: at(tom, 14, 0), mins: 30, c: '#ff375f' }
    ];
  }
  const CAL = { seed: seedEvents(), mine: store.get('events', []), week: 0, sel: null };
  const allEvents = () => [...CAL.seed, ...CAL.mine].sort((a, b) => a.at - b.at);
  const eventsOn = d => allEvents().filter(e => sameDay(new Date(e.at), d));
  function nextMeeting() {
    const now = Date.now();
    return allEvents().find(e => e.at > now && e.at - now <= 15 * 60e3);
  }
  function evRow(e) {
    const now = Date.now();
    const start = new Date(e.at), end = new Date(e.at + e.mins * 60e3);
    const mins = Math.round((e.at - now) / 60e3);
    const soon = e.at > now && mins <= 60 ? `<span class="soon"> · in ${mins} min</span>` : e.at <= now && end > now ? '<span class="soon"> · now</span>' : '';
    return `<div class="ev" style="--c:${e.c}"><i></i><div class="grow"><div style="font-size:14px;font-weight:600">${esc(e.t)}</div><div class="when">${clock(start)} – ${clock(end)}${soon}</div></div></div>`;
  }
  const calView = {
    html() {
      const today = dayStart(new Date());
      const base = addDays(today, CAL.week * 7 - today.getDay());
      const sel = CAL.sel ? new Date(CAL.sel) : today;
      const days = Array.from({ length: 7 }, (_, k) => {
        const d = addDays(base, k);
        const cls = [sameDay(d, today) ? 'today' : '', d < today ? 'past' : '', sameDay(d, sel) ? 'sel' : '', eventsOn(d).length ? 'has' : ''].join(' ');
        return `<button class="cal-day ${cls}" type="button" data-act="calSel" data-t="${d.getTime()}">${'SMTWTFS'[k]}<b>${d.getDate()}</b></button>`;
      }).join('');
      let list;
      if (sameDay(sel, today)) {
        const rest = eventsOn(today).filter(e => e.at + e.mins * 60e3 > Date.now());
        const tom = eventsOn(addDays(today, 1));
        list = rest.length + tom.length === 0
          ? `<div class="row" style="padding:6px 2px"><span class="dotico" style="background:rgba(48,209,88,.25);color:var(--n-green);width:24px;height:24px">${ic('check')}</span><span class="n-sec" style="font-size:15px">Nothing else today or tomorrow</span></div>`
          : `${rest.length ? `<div class="n-cap" style="margin-bottom:4px">Today</div>${rest.map(evRow).join('')}` : ''}${tom.length ? `<div class="n-cap" style="margin:10px 0 4px">Tomorrow</div>${tom.map(evRow).join('')}` : ''}`;
      } else {
        const evs = eventsOn(sel);
        list = `<div class="n-cap" style="margin-bottom:4px">${sel.toLocaleDateString([], { weekday: 'long', month: 'short', day: 'numeric' })}</div>${evs.length ? evs.map(evRow).join('') : '<div class="n-sec" style="padding:6px 0">No events</div>'}`;
      }
      return `<div class="n-card"><div class="cal-head"><span class="cal-month">${base.toLocaleDateString([], { month: 'long', year: 'numeric' })}</span>
          <span class="row" style="gap:2px"><button class="n-icon" type="button" data-act="calAdd" aria-label="New event">${ic('plus')}</button><button class="n-icon" type="button" data-act="calWeek" data-d="-1" aria-label="Previous week">${ic('chevron-left')}</button><button class="n-icon" type="button" data-act="calWeek" data-d="1" aria-label="Next week">${ic('chevron-right')}</button></span></div>
          <div class="cal-week">${days}</div></div>
        <div class="n-card" style="margin-top:10px">${list}</div>`;
    }
  };

  // ======================================================================
  //  TIMER
  // ======================================================================
  const TM = { end: 0, total: 0, left: 0, running: false, paused: false, done: false, to: 0 };
  let actx = null;
  function audioCtx() { actx = actx || new (window.AudioContext || window.webkitAudioContext)(); if (actx.state === 'suspended') actx.resume(); return actx; }
  function chime() {
    try {
      const a = audioCtx(), peak = S.volume / 100 * .35;
      [880, 1174.66, 1567.98, 1174.66, 1567.98].forEach((f, i) => {
        const o = a.createOscillator(), g = a.createGain();
        o.type = 'sine'; o.frequency.value = f;
        const t = a.currentTime + i * .16;
        g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(peak, t + .02); g.gain.exponentialRampToValueAtTime(.0001, t + 1.1);
        o.connect(g).connect(a.destination); o.start(t); o.stop(t + 1.2);
      });
    } catch { /* no audio */ }
  }
  const timeLeft = () => TM.running ? (TM.paused ? TM.left : Math.max(0, (TM.end - Date.now()) / 1000)) : 0;
  function armTimer() { clearTimeout(TM.to); TM.to = setTimeout(timerDone, TM.end - Date.now()); }
  function startTimer(sec) {
    audioCtx();
    Object.assign(TM, { total: sec, end: Date.now() + sec * 1000, running: true, paused: false, done: false });
    armTimer();
    renderPeek(); refresh();
  }
  function timerDone() {
    Object.assign(TM, { running: false, paused: false, done: true });
    chime();
    renderPeek();
    const wasOpen = S.mode === 'panel';
    S.tab = 'timer';
    if (wasOpen) renderPanel(true); else { S.sticky = true; open(); }
    setTimeout(() => {
      TM.done = false;
      if (S.tab === 'timer' && S.mode === 'panel') refresh();
      if (!wasOpen && S.sticky && !notch.matches(':hover')) close();
    }, 5000);
  }
  function timerPaint() {
    const fg = $('#tmFg');
    if (!fg) return;
    const left = timeLeft();
    const C2 = 2 * Math.PI * 56;
    fg.style.strokeDashoffset = TM.running ? C2 * (1 - left / TM.total) : TM.done ? C2 : 0;
    $('#tmLbl').textContent = mss(Math.ceil(left));
  }
  const timerView = {
    html() {
      const C2 = 2 * Math.PI * 56;
      const left = timeLeft();
      const state = TM.done ? 'Done' : TM.paused ? 'Paused' : TM.running ? 'Remaining' : 'Ready';
      const side = TM.running
        ? `<div class="n-cap">Countdown</div><div class="presets" style="grid-template-columns:repeat(3,1fr)">
            <button class="n-btn" type="button" data-act="tmAdd">${ic('plus')}1 min</button>
            <button class="n-btn white" type="button" data-act="tmPause">${ic(TM.paused ? 'play' : 'pause')}${TM.paused ? 'Resume' : 'Pause'}</button>
            <button class="n-btn red" type="button" data-act="tmCancel">${ic('x')}Cancel</button></div>
            <p class="n-ter" style="font-size:12px;margin:10px 2px 0">It shows in the peek while it runs, then chimes and opens when it's done.</p>`
        : `<div class="n-cap">Start a timer</div><div class="presets">${[1, 5, 10, 15, 25, 45].map(m => `<button class="n-btn" type="button" data-act="tmGo" data-s="${m * 60}">${m} min</button>`).join('')}</div>
            <button class="t-demo" type="button" data-act="tmGo" data-s="10">Or try a 10-second one →</button>`;
      return `<div class="timer"><div class="t-ring${TM.done ? ' done' : ''}"><svg viewBox="0 0 128 128"><circle class="bg" cx="64" cy="64" r="56"/><circle class="fg" id="tmFg" cx="64" cy="64" r="56" stroke-dasharray="${C2}" stroke-dashoffset="${TM.running ? C2 * (1 - left / TM.total) : TM.done ? C2 : C2}"/></svg>
        <div class="lbl"><b id="tmLbl">${mss(Math.ceil(left))}</b><span>${state}</span></div></div><div class="grow">${side}</div></div>`;
    }
  };

  // ======================================================================
  //  KEEP AWAKE
  // ======================================================================
  // The browser can't hold off the computer's sleep, but it can keep the screen on while this page shows:
  // the Screen Wake Lock, where there is one.
  const AW = { on: false, end: 0, total: 0, display: store.get('awDisplay', true), to: 0, lock: null };
  const AW_PRESETS = [15, 30, 60, 120, 240];
  const awName = m => m >= 60 ? `${m / 60} hour${m === 60 ? '' : 's'}` : `${m} min`;
  const hms = s => { s = Math.max(0, Math.ceil(s)); return s >= 3600 ? `${Math.floor(s / 3600)}:${pad(Math.floor(s / 60) % 60)}:${pad(s % 60)}` : mss(s); };
  const awLeft = () => AW.on && AW.end ? Math.max(0, (AW.end - Date.now()) / 1000) : 0;
  async function awLock() {
    const want = AW.on && AW.display;
    if (want && !AW.lock && navigator.wakeLock) {
      try { AW.lock = await navigator.wakeLock.request('screen'); AW.lock.addEventListener('release', () => { AW.lock = null; }); } catch { /* not allowed here */ }
    } else if (!want && AW.lock) {
      AW.lock.release(); AW.lock = null;
    }
  }
  function awStart(mins) {
    clearTimeout(AW.to);
    Object.assign(AW, { on: true, total: mins * 60, end: mins ? Date.now() + mins * 60e3 : 0 });
    if (mins) AW.to = setTimeout(awStop, mins * 60e3);
    awLock(); renderPeek(); refresh();
  }
  function awStop() {
    clearTimeout(AW.to);
    Object.assign(AW, { on: false, end: 0, total: 0 });
    awLock(); renderPeek(); refresh();
  }
  function awPaint() {
    const fg = $('#awFg');
    if (!fg) return;
    const C2 = 2 * Math.PI * 56;
    fg.style.strokeDashoffset = AW.end ? C2 * (1 - awLeft() / AW.total) : AW.on ? 0 : C2;
    $('#awLbl').textContent = AW.end ? hms(awLeft()) : AW.on ? 'On' : 'Off';
  }
  const awakeView = {
    html() {
      const C2 = 2 * Math.PI * 56;
      const offset = AW.end ? C2 * (1 - awLeft() / AW.total) : AW.on ? 0 : C2;
      const cap = !AW.on ? 'Click to start' : AW.end ? `Until ${clock(new Date(AW.end))}` : 'Until you stop';
      const pick = (on, act, data, body) => `<button class="n-btn${on ? ' yellow' : ''}" type="button" data-act="${act}"${data}>${body}</button>`;
      const presets = [pick(AW.on && !AW.end, 'awGo', ' data-m="0"', `${ic('infinity')}Until I stop`)]
        .concat(AW_PRESETS.map(m => pick(AW.on && AW.total === m * 60, 'awGo', ` data-m="${m}"`, awName(m)))).join('');
      const options = (AW.on && AW.end ? `<button class="n-btn" type="button" data-act="awMore">${ic('plus')}15 min</button>` : '')
        + (AW.on ? `<button class="n-btn red" type="button" data-act="awStop">${ic('x')}Stop</button>` : '')
        + pick(AW.display, 'awDisplay', '', `${ic('monitor')}Screen stays on`);
      return `<div class="timer"><button class="t-ring awake${AW.on ? ' on' : ''}" type="button" data-act="awToggle" aria-label="${AW.on ? 'Stop keeping awake' : 'Keep awake until I stop'}"><svg viewBox="0 0 128 128"><circle class="bg" cx="64" cy="64" r="56"/><circle class="fg" id="awFg" cx="64" cy="64" r="56" stroke-dasharray="${C2}" stroke-dashoffset="${offset}"/></svg>
        <span class="lbl"><span class="cup">${ic('coffee')}</span><b id="awLbl">${AW.end ? hms(awLeft()) : AW.on ? 'On' : 'Off'}</b><span>${cap}</span></span></button>
        <div class="grow"><div class="n-cap">Keep this computer awake</div><div class="presets">${presets}</div><div class="aw-opts">${options}</div></div></div>`;
    }
  };

  // ======================================================================
  //  CALCULATOR
  // ======================================================================
  const CA = { toks: [], cur: '0', fresh: true, last: null, hist: store.get('calc', [{ e: '0.1 + 0.2', v: '0.3' }, { e: '1,299 × 12', v: '15588' }]) };
  const OPS = { '+': 1, '−': 1, '×': 2, '÷': 2 };
  const tidy = x => Number.isFinite(x) ? String(Number(x.toPrecision(12))) : 'Error';
  function group(s) {
    if (s === 'Error') return s;
    const neg = s.startsWith('-');
    const [i, f] = s.replace('-', '').split('.');
    const g = i.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    return (neg ? '-' : '') + g + (f !== undefined ? '.' + f : '');
  }
  function evaluate(toks) {
    const out = [toks[0]];
    for (let k = 1; k < toks.length; k += 2) {
      const op = toks[k], n = toks[k + 1];
      if (OPS[op] === 2) { const a = out.pop(); out.push(op === '×' ? a * n : a / n); }
      else { out.push(op, n); }
    }
    let v = out[0];
    for (let k = 1; k < out.length; k += 2) v = out[k] === '+' ? v + out[k + 1] : v - out[k + 1];
    return v;
  }
  function calcKey(k) {
    if (/^[0-9]$/.test(k)) { CA.cur = CA.fresh || CA.cur === '0' ? k : CA.cur.length < 15 ? CA.cur + k : CA.cur; CA.fresh = false; CA.last = null; }
    else if (k === '.') { CA.cur = CA.fresh ? '0.' : CA.cur.includes('.') ? CA.cur : CA.cur + '.'; CA.fresh = false; }
    else if (k === 'AC') { CA.toks = []; CA.cur = '0'; CA.fresh = true; CA.last = null; }
    else if (k === 'C') { CA.cur = '0'; CA.fresh = true; }
    else if (k === '±') { CA.cur = CA.cur.startsWith('-') ? CA.cur.slice(1) : CA.cur === '0' ? '0' : '-' + CA.cur; }
    else if (k === '%') { CA.cur = tidy(parseFloat(CA.cur) / 100); }
    else if (OPS[k]) {
      if (CA.fresh && CA.toks.length && OPS[CA.toks[CA.toks.length - 1]]) CA.toks[CA.toks.length - 1] = k;
      else { CA.toks.push(parseFloat(CA.cur), k); CA.fresh = true; }
      CA.last = k;
    } else if (k === '=') {
      if (!CA.toks.length) return;
      const toks = [...CA.toks, parseFloat(CA.cur)];
      const expr = toks.map(t => typeof t === 'number' ? group(tidy(t)) : t).join(' ');
      const v = tidy(evaluate(toks));
      CA.hist.unshift({ e: expr, v }); CA.hist = CA.hist.slice(0, 20); store.set('calc', CA.hist);
      CA.toks = []; CA.cur = v; CA.fresh = true; CA.last = null;
    }
    refresh();
  }
  const calcView = {
    html() {
      const expr = CA.toks.map(t => typeof t === 'number' ? group(tidy(t)) : t).join(' ');
      const key = (k, cls = '') => `<button class="ck ${cls}${CA.last === k && CA.fresh ? ' sel' : ''}" type="button" data-act="ck" data-k="${k}">${k}</button>`;
      const hasEntry = CA.cur !== '0' && !CA.fresh;
      return `<div class="calc"><div>
          <div class="c-display"><div class="row"><button class="n-icon" type="button" data-act="cCopy" aria-label="Copy the number">${ic('copy')}</button><div class="c-expr grow">${esc(expr)}</div></div><div class="c-val">${group(CA.cur)}</div></div>
          <div class="c-keys">${key(hasEntry ? 'C' : 'AC', 'fn')}${key('±', 'fn')}${key('%', 'fn')}${key('÷', 'op')}${key('7')}${key('8')}${key('9')}${key('×', 'op')}${key('4')}${key('5')}${key('6')}${key('−', 'op')}${key('1')}${key('2')}${key('3')}${key('+', 'op')}${key('0', 'zero')}${key('.')}${key('=', 'op')}</div>
        </div>
        <div class="c-hist"><div class="n-cap">Recent</div><ul>${CA.hist.map((h, i) => `<li data-act="cHist" data-i="${i}"><span>${esc(h.e)}</span>${group(h.v)}</li>`).join('') || '<li class="n-ter">Your sums show here</li>'}</ul></div></div>`;
    }
  };

  // ======================================================================
  //  TELEPROMPTER
  // ======================================================================
  const DEFAULT_SCRIPT = "Welcome to Notcho.\n\nThis is the teleprompter. Your script scrolls right under the camera, so your eyes stay on the lens while you read.\n\nPress the pencil to put your own words here. Slow it down with the turtle, speed it up with the rabbit, and make the text bigger when you sit back from the desk.\n\nThe notch stays open while it runs. Pause it and it folds back into the camera like it was never there.";
  const SPEEDS = [6, 12, 18, 24, 36, 48];
  const PR = { script: store.get('script', DEFAULT_SCRIPT), running: false, y: 0, sp: 2, fs: 24, raf: 0, last: 0 };
  function prLoop(t) {
    const el = $('#prScript');
    if (!el || !PR.running) return;
    const dt = PR.last ? (t - PR.last) / 1000 : 0;
    PR.last = t;
    PR.y += SPEEDS[PR.sp] * 1.6 * dt;
    const max = el.scrollHeight - 140;
    if (PR.y >= max) { PR.y = max; PR.running = false; refresh(); return; }
    el.style.transform = `translateY(${-PR.y}px)`;
    PR.raf = requestAnimationFrame(prLoop);
  }
  function prPlay() {
    PR.running = !PR.running;
    if (PR.running) { const el = $('#prScript'); if (el && PR.y >= el.scrollHeight - 141) PR.y = 0; }
    refresh();
  }
  const prompterView = {
    html() {
      return `<div class="prompter"><div class="script" id="prScript" style="--fs:${PR.fs}px;transform:translateY(${-PR.y}px)">${esc(PR.script)}</div></div>
        <div class="p-ctl">
          <span class="grp"><button class="n-icon" type="button" data-act="prTop" aria-label="Back to the start">${ic('skip-back')}</button><button class="n-icon" type="button" data-act="prNudge" data-d="-40" aria-label="Nudge back">${ic('chevron-up')}</button><button class="n-icon" type="button" data-act="prNudge" data-d="40" aria-label="Nudge forward">${ic('chevron-down')}</button></span>
          <button class="p-play" type="button" data-act="prPlay" aria-label="${PR.running ? 'Pause' : 'Play'}">${ic(PR.running ? 'pause' : 'play')}</button>
          <span class="grp"><button class="n-icon" type="button" data-act="prSpeed" data-d="-1" aria-label="Slower">${ic('turtle')}</button><b class="tnum n-sec" style="width:58px;text-align:center">${SPEEDS[PR.sp]} pt/s</b><button class="n-icon" type="button" data-act="prSpeed" data-d="1" aria-label="Faster">${ic('rabbit')}</button>
            <button class="n-icon" type="button" data-act="prSize" data-d="-2" aria-label="Smaller text" style="font:700 12px var(--sf)">A</button><button class="n-icon" type="button" data-act="prSize" data-d="2" aria-label="Bigger text" style="font:700 18px var(--sf)">A</button>
            <button class="n-icon" type="button" data-act="prEdit" aria-label="Edit the script">${ic('square-pen')}</button></span>
        </div>`;
    },
    mount() { if (PR.running) { PR.last = 0; PR.raf = requestAnimationFrame(prLoop); } },
    unmount() { cancelAnimationFrame(PR.raf); }
  };

  const VIEWS = {
    media: mediaView, weather: weatherView, sports: sportsView, news: newsView, rss: rssView, tray: trayView, stats: statsView, processes: procView, capture: captureView,
    mirror: mirrorView, notes: notesView, tasks: tasksView, clipboard: clipView, calendar: calView, timer: timerView,
    awake: awakeView, calculator: calcView, prompter: prompterView
  };

  // ======================================================================
  //  PEEK / PANEL / HUD
  // ======================================================================
  const peekParts = () => withCup(peekBase());
  // A cup ahead of whatever ends the right wing, while keeping awake.
  function withCup(p) {
    if (!AW.on) return p;
    return { ...p, rk: `${p.rk}|cup`, right: `<span class="ico" style="color:var(--n-yellow)">${ic('coffee')}</span>${p.right}` };
  }
  function peekBase() {
    const tr = track();
    const left = `<span class="p-art" style="background:${artBg(tr)}"></span><span class="p-bars ${M.playing ? 'on' : ''}" style="--accent:${tr.c}"><i></i><i></i><i></i><i></i></span>`;
    if (C.recording) return { left, rk: 'rec', right: `<span class="rec"></span><span id="pkR">${mss((Date.now() - C.recStart) / 1000)}</span>` };
    if (N.rec) return { left, rk: 'rec', right: `<span class="rec"></span><span id="pkR">${mss((Date.now() - N.start) / 1000)}</span>` };
    if (TM.running) return { left, rk: 'timer', right: `<span class="ico" style="color:var(--n-orange)">${ic('timer')}</span><span id="pkR" style="color:var(--n-orange)">${mss(Math.ceil(timeLeft()))}</span>` };
    const mt = nextMeeting();
    if (mt) return { left, rk: 'meet', right: `<span class="ico meet">${ic('calendar-days')}</span><span id="pkR">in ${Math.max(1, Math.round((mt.at - Date.now()) / 60e3))} min</span>` };
    const w = W.data[PLACES[0].n];
    if (w) {
      const c = wCode(w.now.code, w.now.day);
      return { left, rk: `w${w.now.code}${w.now.day}`, right: `<span class="ico" style="color:${c.y ? 'var(--n-yellow)' : '#fff'}">${ic(c.i)}</span><span id="pkR">${deg(w.now.t)}</span>` };
    }
    return { left, rk: 'none', right: '' };
  }
  let peekKey = '';
  function renderPeek() {
    const p = peekParts();
    const key = `${M.i}|${M.playing}|${p.rk}`;
    const r = $('#pkR', L.peek);
    if (key === peekKey && r) {
      const tmp = document.createElement('div');
      tmp.innerHTML = p.right;
      r.textContent = $('#pkR', tmp).textContent;
      return;
    }
    peekKey = key;
    L.peek.innerHTML = `<div class="wing l" style="width:${WING_W}px">${p.left}</div><div class="cam" style="width:${CAM_W}px"></div><div class="wing r" style="width:${WING_W}px">${p.right}</div>`;
    if (S.mode === 'peek') sizeTo();
  }
  function renderHud(kind) {
    const v = kind === 'vol' ? S.volume : S.bright;
    const icon = kind === 'vol' ? (v === 0 ? 'volume-x' : v < 50 ? 'volume-1' : 'volume-2') : (v < 50 ? 'sun-dim' : 'sun');
    L.hud.innerHTML = `<span class="ico">${ic(icon)}</span><span style="width:${CAM_W}px"></span><span class="hud-r"><span class="hud-bar"><i style="width:${v}%"></i></span><span class="hud-n">${v}</span></span>`;
    L.hud.style.width = (CAM_W + 2 * WING_W + 40) + 'px';
  }
  function showHud(kind) {
    if ((kind === 'vol' && !S.hudVol) || (kind === 'bright' && !S.hudBright)) return;
    if (S.mode === 'panel') { renderControls(); return; }
    renderHud(kind);
    if (S.mode !== 'hud') setMode('hud');
    clearTimeout(hudTimer);
    hudTimer = setTimeout(() => { if (S.mode === 'hud') setMode('peek'); }, 1500);
  }

  const visibleTabs = () => TABS.filter(t => !S.hidden.includes(t.id));
  function renderPanel(fresh) {
    if (S.hidden.includes(S.tab)) S.tab = (visibleTabs()[0] || TABS[0]).id;
    // As in the app: one strip, and the open tab carries its name.
    const btn = t => `<button class="tab${S.tab === t.id ? ' on' : ''}" type="button" data-act="tab" data-id="${t.id}" title="${t.name}" aria-label="${t.name}">${ic(t.icon)}${S.tab === t.id ? `<span>${t.name}</span>` : ''}</button>`;
    L.panel.innerHTML = `<div class="tabs"><div class="grp">${visibleTabs().map(btn).join('')}</div>
      <div class="grp">
        <button class="tab pin${S.pinned ? ' on' : ''}" type="button" data-act="pin" title="Keep open" aria-label="Keep the panel open" aria-pressed="${S.pinned}">${ic('pin')}</button>
        <button class="tab" type="button" data-act="settings" title="Settings" aria-label="Settings">${ic('settings')}</button></div></div>
      <div class="body" id="pBodyWrap"></div><div class="controls" id="pCtl"></div>`;
    renderBody(fresh);
    renderControls();
  }
  function renderBody(fresh) {
    if (view && view.unmount) view.unmount();
    const wrap = $('#pBodyWrap');
    if (!wrap) return;
    const v = VIEWS[S.tab];
    if (fresh) v.swap = false;
    wrap.innerHTML = `<div class="view${fresh ? '' : ' stay'}">${v.html()}</div>`;
    view = v;
    if (v.mount) v.mount(wrap);
  }
  function refresh(swap) {
    if (S.mode !== 'panel') return;
    VIEWS[S.tab].swap = !!swap;
    renderBody(false);
  }
  const warm = (v, h0, h1) => `hsl(${h0 + (h1 - h0) * v / 100} 85% 70%)`;
  function renderControls() {
    const el = $('#pCtl');
    if (!el) return;
    const vc = warm(S.volume, 232, 320), bc = warm(S.bright, 48, 26);
    el.innerHTML = `<button class="n-btn pp" type="button" data-act="mPlay" aria-label="${M.playing ? 'Pause' : 'Play'}">${ic(M.playing ? 'pause' : 'play')}</button>
      <label class="ctl">${ic(S.volume === 0 ? 'volume-x' : 'volume-1')}<input class="slider" id="ctlVol" type="range" min="0" max="100" value="${S.volume}" style="--c:${vc};--v:${S.volume}%" aria-label="Volume"></label>
      <label class="ctl">${ic('sun')}<input class="slider" id="ctlBright" type="range" min="10" max="100" value="${S.bright}" style="--c:${bc};--v:${(S.bright - 10) / 90 * 100}%" aria-label="Brightness"></label>`;
  }
  function setVolume(v) { S.volume = clamp(Math.round(v), 0, 100); if (N.audio) N.audio.volume = S.volume / 100; }
  function setBright(v) { S.bright = clamp(Math.round(v), 10, 100); $('#dim').style.opacity = ((100 - S.bright) / 90 * .72).toFixed(3); }

  function sizeTo() {
    const layer = L[S.mode];
    const w = Math.max(CAM_W, layer.offsetWidth), h = Math.max(34, layer.offsetHeight);
    notch.style.width = w + 'px';
    notch.style.height = h + 'px';
    let s = 1;
    if (S.mode === 'panel') s = Math.min(1, (innerWidth - 12) / w, (innerHeight - 12) / h);
    notch.style.setProperty('--s', s.toFixed(3));
  }
  function setMode(m) {
    const was = S.mode;
    S.mode = m;
    Object.entries(L).forEach(([k, el]) => el.classList.toggle('on', k === m));
    notch.classList.toggle('is-panel', m === 'panel');
    notch.classList.toggle('is-hud', m === 'hud');
    if (was === 'panel' && m !== 'panel') {
      if (view && view.unmount) view.unmount();
      view = null;
      setTimeout(() => { if (S.mode !== 'panel') L.panel.innerHTML = ''; }, 320);
    }
    if (m === 'panel' && was !== 'panel') renderPanel(true);
    if (m === 'peek') renderPeek();
    sizeTo();
  }
  function open() {
    if (S.mode === 'panel') return;
    setMode('panel');
    if (!S.opened) {
      S.opened = true;
      notch.classList.remove('attract');
    }
  }
  function close() { S.sticky = false; if (S.mode === 'panel') setMode('peek'); }
  const keepOpen = () => S.pinned || N.rec || PR.running || dragActive || !$('#entry').hidden || TM.done;
  function openTab(id) {
    S.hidden = S.hidden.filter(h => h !== id);
    const changed = S.tab !== id;
    S.tab = id; store.set('tab', id);
    S.sticky = true;
    if (S.mode === 'panel') { if (changed) renderPanel(true); }
    else open();
  }

  // hover: open after the hover delay, fold back shortly after leaving
  let openT = 0, closeT = 0;
  notch.addEventListener('pointerenter', e => {
    if (e.pointerType === 'touch') return;
    clearTimeout(closeT);
    if (S.mode !== 'panel') openT = setTimeout(open, S.hoverDelay);
  });
  notch.addEventListener('pointerleave', e => {
    if (e.pointerType === 'touch') return;
    clearTimeout(openT);
    S.sticky = false;
    clearTimeout(closeT);
    closeT = setTimeout(() => { if (!keepOpen() && !notch.matches(':hover')) close(); }, 380);
  });
  notch.addEventListener('click', e => { if (S.mode !== 'panel' && !e.target.closest('[data-act]')) open(); });
  notch.addEventListener('keydown', e => { if ((e.key === 'Enter' || e.key === ' ') && e.target === notch && S.mode !== 'panel') { e.preventDefault(); S.sticky = true; open(); } });
  document.addEventListener('pointerdown', e => {
    if (S.mode !== 'panel' || keepOpen()) return;
    if (e.target.closest('#notchWrap, #entry, #settings, #snap, .toasts, [data-open-tab], [data-key], .dfile, .chip')) return;
    close();
  });
  document.addEventListener('keydown', e => {
    const typing = e.target.closest('input, textarea, select, [contenteditable]');
    if (e.key === 'Escape' && !$('#settings').hidden) { $('#settings').hidden = true; return; }
    if (e.key === 'Escape' && $('#entry').hidden && S.mode === 'panel' && !S.pinned) close();
    if (typing) return;
    const map = { '-': 'vol-down', '=': 'vol-up', '+': 'vol-up', '[': 'bright-down', ']': 'bright-up', F1: 'bright-down', F2: 'bright-up', F11: 'vol-down', F12: 'vol-up' };
    if (map[e.key]) { pressKey(map[e.key]); const k = $(`.key[data-key="${map[e.key]}"]`); if (k) { k.classList.add('press'); setTimeout(() => k.classList.remove('press'), 120); } }
  });
  function pressKey(k) {
    if (k === 'vol-up') { setVolume(S.volume + 6.25); showHud('vol'); }
    if (k === 'vol-down') { setVolume(S.volume - 6.25); showHud('vol'); }
    if (k === 'bright-up') { setBright(S.bright + 6.25); showHud('bright'); }
    if (k === 'bright-down') { setBright(S.bright - 6.25); showHud('bright'); }
  }

  new ResizeObserver(() => { if (S.mode === 'panel') sizeTo(); }).observe(L.panel);
  addEventListener('resize', () => { fitNotch(); peekKey = ''; renderPeek(); sizeTo(); });

  // ---------- panel actions ----------
  const A = {
    tab: b => { if (S.tab === b.dataset.id) return; S.tab = b.dataset.id; store.set('tab', S.tab); renderPanel(true); },
    pin: () => { S.pinned = !S.pinned; renderPanel(false); toast(S.pinned ? 'Pinned: the panel stays open' : 'Unpinned', { icon: 'pin' }); },
    settings: () => openSettings(),
    mPlay: () => { M.playing = !M.playing; onMediaChanged(false); },
    mPrev: () => mediaSkip(-1),
    mNext: () => mediaSkip(1),
    mScrub: (b, e) => { const r = b.getBoundingClientRect(); M.pos = Math.round(clamp((e.clientX - r.left) / r.width, 0, 1) * (track().d - 1)); refresh(); },
    spFilter: b => { SP.filter = b.dataset.id; refresh(); },
    nwFilter: b => { NW.filter = b.dataset.id; refresh(); },
    rsFilter: b => { RS.filter = b.dataset.id; refresh(); },
    spPage: b => { SP.page = b.dataset.id; refresh(); },
    spGame: b => { SP.game = Number(b.dataset.id); SP.page = 'game'; refresh(); },
    spBack: () => { SP.page = 'scores'; refresh(); },
    spDay: () => toast('The app steps through the days with ‹ ›', { icon: 'calendar-days' }),
    wPrev: () => { W.i = (W.i - 1 + PLACES.length) % PLACES.length; refresh(); },
    wNext: () => { W.i = (W.i + 1) % PLACES.length; refresh(); },
    wUnit: () => { S.unit = useF() ? 'c' : 'f'; store.set('unit', S.unit); refresh(); renderPeek(); },
    tCopy: b => { const f = T.files.find(x => x.id === b.dataset.id); copyText(f.name); toast(`Copied “${f.name}”. ⌘V in Finder pastes it.`, { icon: 'copy' }); },
    tReveal: b => { const f = T.files.find(x => x.id === b.dataset.id); toast(`Revealed “${f.name}” in Finder`, { icon: 'folder' }); },
    tRemove: b => { const f = T.files.find(x => x.id === b.dataset.id); T.files = T.files.filter(x => x !== f); restoreDesk(f); refresh(); },
    tCopyAll: () => { copyText(T.files.map(f => f.name).join('\n')); toast(`Copied ${T.files.length} file${T.files.length === 1 ? '' : 's'}. ⌘V in Finder pastes them.`, { icon: 'copy' }); },
    tAirDrop: () => { toast('Looking for AirDrop devices…', { radar: true, ms: 2000 }); setTimeout(() => toast('On your Mac this opens the AirDrop sheet', { icon: 'radio' }), 2100); },
    tRevealAll: () => toast('Revealed in Finder', { icon: 'folder' }),
    tClear: () => { T.files.forEach(restoreDesk); T.files = []; refresh(); },
    pSort: b => { const k = b.dataset.k; P.desc = P.sort === k ? !P.desc : k !== 'n'; P.sort = k; refresh(); },
    pKill: b => {
      const id = +b.dataset.id;
      const p = PROCS.find(x => x.id === id);
      if (P.ask !== id) { P.ask = id; clearTimeout(P.askT); P.askT = setTimeout(() => { P.ask = -1; const pb = $('#pBody'); if (pb) pb.innerHTML = procRows(); }, 3000); $('#pBody').innerHTML = procRows(); return; }
      P.ask = -1;
      if (p.n === 'Notcho') { toast('Nice try. Notcho stays.', { icon: 'shield' }); $('#pBody').innerHTML = procRows(); return; }
      const row = $(`tr[data-pid="${id}"]`);
      if (row) row.classList.add('gone');
      setTimeout(() => { PROCS.splice(PROCS.indexOf(p), 1); const pb = $('#pBody'); if (pb) pb.innerHTML = procRows(); }, 300);
      toast(`Force quit ${p.n}`, { icon: 'x' });
    },
    cap: b => capture(b.dataset.k),
    mrCam: () => startCamera(),
    mrLook: b => {
      MR.look = (MR.look + +b.dataset.d + LOOKS.length) % LOOKS.length;
      $('#mrLook').textContent = LOOKS[MR.look][0];
      const target = $('#mrVideo') || $('#mrCanvas');
      target.style.filter = mirrorFilter();
    },
    mrShot: () => shutter(),
    nRec: () => toggleRecord(),
    nMic: () => { N.mic = (N.mic + 1) % MICS.length; refresh(); },
    nPlay: b => playNote(b.dataset.id),
    nTray: b => { const n = N.notes.find(x => x.id === b.dataset.id); addToTray({ name: `Voice note ${stamp(new Date(n.at))}.m4a`, ext: 'M4A', c: '#ff375f' }); },
    nReveal: () => toast('Revealed in Finder', { icon: 'folder' }),
    nDel: b => { if (N.playing === b.dataset.id) stopPlayback(); N.notes = N.notes.filter(x => x.id !== b.dataset.id); refresh(); },
    tkCheck: b => { const x = TK.list.find(t => t.id === b.dataset.id); x.done = !x.done; x.doneAt = Date.now(); saveTasks(); refresh(); },
    tkDel: b => { TK.list = TK.list.filter(t => t.id !== b.dataset.id); saveTasks(); refresh(); },
    tkAdd: () => openEntry({
      label: 'New task', placeholder: 'What needs doing?', hint: 'Return adds it and clears the box for the next. Esc closes.',
      submit: v => { TK.list.unshift({ id: uid(), t: v, done: false }); saveTasks(); refresh(); return true; }
    }),
    cbCopy: b => {
      const c = CB.list.find(x => x.id === b.dataset.id);
      copyText(c.t);
      c.at = Date.now();
      CB.list = [c, ...CB.list.filter(x => x !== c)]; saveClips();
      refresh();
      toast('Back on the clipboard, ready to paste', { icon: 'clipboard-check' });
    },
    cbPin: b => { const c = CB.list.find(x => x.id === b.dataset.id); c.pinned = !c.pinned; saveClips(); refresh(); },
    cbDel: b => { CB.list = CB.list.filter(x => x.id !== b.dataset.id); saveClips(); refresh(); },
    cbClear: () => { CB.list = CB.list.filter(x => x.pinned); saveClips(); refresh(); },
    calSel: b => { CAL.sel = +b.dataset.t; refresh(); },
    calWeek: b => { CAL.week += +b.dataset.d; refresh(); },
    calAdd: () => {
      const today = dayStart(new Date());
      const sel = CAL.sel ? new Date(CAL.sel) : today;
      const dayOpts = [today, addDays(today, 1), ...(sel > addDays(today, 1) ? [sel] : [])].map(d => `<option value="${d.getTime()}"${sameDay(d, sel) ? ' selected' : ''}>${dayLabel(d)}${sameDay(d, today) || sameDay(d, addDays(today, 1)) ? '' : ' ' + d.getDate()}</option>`).join('');
      const nextHalf = Math.ceil((new Date().getHours() * 60 + new Date().getMinutes() + 1) / 30) * 30;
      const times = Array.from({ length: 48 }, (_, k) => k * 30).map(m => { const d = new Date(today.getTime() + m * 60e3); return `<option value="${m}"${m === Math.min(nextHalf, 1410) ? ' selected' : ''}>${clock(d)}</option>`; }).join('');
      openEntry({
        label: 'New event', placeholder: 'Coffee with Sam', hint: 'Return adds it to your calendar. Esc closes.',
        extra: `<select id="evDay" aria-label="Day">${dayOpts}</select><select id="evTime" aria-label="Time">${times}</select><select id="evLen" aria-label="Length"><option value="30">30 min</option><option value="60" selected>1 hour</option><option value="90">1.5 hours</option></select>`,
        submit: v => {
          const at = +$('#evDay').value + +$('#evTime').value * 60e3;
          const colors = ['#0a84ff', '#30d158', '#ff9f0a', '#bf5af2', '#ff375f', '#64d2ff'];
          CAL.mine.push({ id: uid(), t: v, at, mins: +$('#evLen').value, c: colors[CAL.mine.length % colors.length] });
          store.set('events', CAL.mine);
          CAL.sel = dayStart(new Date(at)).getTime();
          refresh(); renderPeek();
          toast(`Added “${v}”, ${dayLabel(new Date(at))} at ${clock(new Date(at))}`, { icon: 'calendar-plus' });
          return false;
        }
      });
    },
    tmGo: b => startTimer(+b.dataset.s),
    tmAdd: () => { if (TM.paused) TM.left += 60; else { TM.end += 60e3; armTimer(); } TM.total += 60; timerPaint(); renderPeek(); },
    tmPause: () => {
      if (TM.paused) { TM.end = Date.now() + TM.left * 1000; TM.paused = false; armTimer(); }
      else { TM.left = timeLeft(); TM.paused = true; clearTimeout(TM.to); }
      refresh(); renderPeek();
    },
    tmCancel: () => { clearTimeout(TM.to); Object.assign(TM, { running: false, paused: false }); refresh(); renderPeek(); },
    awGo: b => awStart(+b.dataset.m),
    awToggle: () => { if (AW.on) awStop(); else awStart(0); },
    awStop: () => awStop(),
    awMore: () => { AW.end += 15 * 60e3; AW.total += 15 * 60; clearTimeout(AW.to); AW.to = setTimeout(awStop, AW.end - Date.now()); refresh(); },
    awDisplay: () => { AW.display = !AW.display; store.set('awDisplay', AW.display); awLock(); refresh(); },
    ck: b => calcKey(b.dataset.k),
    cCopy: () => { const raw = CA.cur; copyText(raw); addClip(raw); toast(`Copied ${raw}`, { icon: 'copy' }); },
    cHist: b => { CA.cur = CA.hist[+b.dataset.i].v; CA.fresh = true; refresh(); },
    prPlay: () => prPlay(),
    prTop: () => { PR.y = 0; refresh(); },
    prNudge: b => { const el = $('#prScript'); PR.y = clamp(PR.y + +b.dataset.d, 0, Math.max(0, el.scrollHeight - 140)); el.style.transform = `translateY(${-PR.y}px)`; },
    prSpeed: b => { PR.sp = clamp(PR.sp + +b.dataset.d, 0, SPEEDS.length - 1); refresh(); },
    prSize: b => { PR.fs = clamp(PR.fs + +b.dataset.d, 16, 40); refresh(); },
    prEdit: () => openEntry({
      label: 'Teleprompter script', multiline: true, value: PR.script, hint: '⌘ Return (or Ctrl Return) saves. Esc closes.',
      extra: '<button type="button" id="entrySave">Save script</button><button type="button" id="entryReset">Use the sample</button>',
      submit: v => { PR.script = v; PR.y = 0; store.set('script', v); refresh(); toast('Script saved', { icon: 'square-pen' }); return false; }
    })
  };
  L.panel.addEventListener('click', e => {
    const b = e.target.closest('[data-act]');
    if (!b || b.disabled || !A[b.dataset.act]) return;
    e.stopPropagation();
    A[b.dataset.act](b, e);
  });
  L.panel.addEventListener('input', e => {
    if (e.target.id === 'ctlVol') { setVolume(+e.target.value); e.target.style.setProperty('--v', S.volume + '%'); e.target.style.setProperty('--c', warm(S.volume, 232, 320)); }
    if (e.target.id === 'ctlBright') { setBright(+e.target.value); e.target.style.setProperty('--v', (S.bright - 10) / 90 * 100 + '%'); e.target.style.setProperty('--c', warm(S.bright, 48, 26)); }
  });

  // ---------- quick entry popup ----------
  let entryCfg = null;
  function openEntry(cfg) {
    entryCfg = cfg;
    const box = $('#entry'), inp = $('#entryInput'), area = $('#entryArea');
    $('#entryLabel').textContent = cfg.label;
    $('#entryHint').textContent = cfg.hint || '';
    $('#entryExtra').innerHTML = cfg.extra || '';
    inp.hidden = !!cfg.multiline; area.hidden = !cfg.multiline;
    inp.placeholder = cfg.placeholder || '';
    inp.value = ''; area.value = cfg.value || '';
    $('#entryLabel').setAttribute('for', cfg.multiline ? 'entryArea' : 'entryInput');
    box.hidden = false;
    (cfg.multiline ? area : inp).focus();
    const save = $('#entrySave');
    if (save) save.onclick = submitEntry;
    const reset = $('#entryReset');
    if (reset) reset.onclick = () => { area.value = DEFAULT_SCRIPT; };
  }
  function closeEntry() { $('#entry').hidden = true; entryCfg = null; }
  function submitEntry() {
    if (!entryCfg) return;
    const val = (entryCfg.multiline ? $('#entryArea').value : $('#entryInput').value).trim();
    if (!val) return;
    const keep = entryCfg.submit(val);
    if (keep) { $('#entryInput').value = ''; $('#entryInput').focus(); }
    else closeEntry();
  }
  $('#entryForm').addEventListener('submit', e => { e.preventDefault(); submitEntry(); });
  $('#entryArea').addEventListener('keydown', e => { if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) { e.preventDefault(); submitEntry(); } });
  $('#entry').addEventListener('keydown', e => { if (e.key === 'Escape') { e.stopPropagation(); closeEntry(); } });
  $('#entry').addEventListener('pointerdown', e => { if (e.target.id === 'entry') closeEntry(); });

  // ---------- settings window ----------
  function settingsHTML() {
    const tabRows = TABS.map(t => `<div class="set-row"><label for="st_${t.id}">${ic(t.icon)}${t.name}</label><input class="switch" type="checkbox" id="st_${t.id}" data-set="tab" data-id="${t.id}" ${S.hidden.includes(t.id) ? '' : 'checked'}></div>`).join('');
    const seg = ['auto', 'c', 'f'].map(u => `<button type="button" data-set="unit" data-u="${u}" class="${S.unit === u ? 'on' : ''}">${{ auto: 'Automatic', c: '°C', f: '°F' }[u]}</button>`).join('');
    return `<div class="set-sec"><h5>Notch</h5><div class="set-box">
        <div class="set-row"><span>Hover before opening</span><span class="row"><input class="slider" type="range" id="setDelay" min="0" max="1000" step="50" value="${S.hoverDelay}" style="--v:${S.hoverDelay / 10}%"><b class="tnum" id="setDelayV" style="width:56px;text-align:right">${S.hoverDelay} ms</b></span></div>
        <div class="set-row"><label for="setPin">Keep the panel open (pin)</label><input class="switch" type="checkbox" id="setPin" data-set="pin" ${S.pinned ? 'checked' : ''}></div></div></div>
      <div class="set-sec"><h5>Tabs</h5><div class="set-box">${tabRows}</div></div>
      <div class="set-sec"><h5>Weather</h5><div class="set-box"><div class="set-row"><span>Temperature</span><span class="seg">${seg}</span></div></div></div>
      <div class="set-sec"><h5>HUD</h5><div class="set-box">
        <div class="set-row"><label for="setHv">Show volume changes</label><input class="switch" type="checkbox" id="setHv" data-set="hudVol" ${S.hudVol ? 'checked' : ''}></div>
        <div class="set-row"><label for="setHb">Show brightness changes</label><input class="switch" type="checkbox" id="setHb" data-set="hudBright" ${S.hudBright ? 'checked' : ''}></div></div></div>
      <div class="set-sec"><h5>About</h5><div class="set-box"><div class="set-row"><span>Notcho for the web<br><span class="n-ter" style="font-size:11px">A browser copy. Get the real one for your Mac.</span></span><a class="n-btn white" href="https://github.com/ShinySoftware/Notcho/releases/latest" target="_blank" rel="noopener" style="text-decoration:none">Download</a></div></div></div>`;
  }
  function openSettings() {
    const s = $('#settings');
    $('#settingsBody').innerHTML = settingsHTML();
    s.hidden = false;
    const win = $('#settingsWin');
    win.style.left = ''; win.style.top = ''; win.style.transform = '';
  }
  function afterTabsChanged() { store.set('hidden', S.hidden); if (S.mode === 'panel') renderPanel(false); }
  $('#settingsClose').addEventListener('click', () => { $('#settings').hidden = true; });
  $('#settingsBody').addEventListener('change', e => {
    const el = e.target;
    if (el.dataset.set === 'tab') {
      S.hidden = el.checked ? S.hidden.filter(h => h !== el.dataset.id) : [...S.hidden, el.dataset.id];
      if (!visibleTabs().length) { S.hidden = S.hidden.filter(h => h !== el.dataset.id); el.checked = true; toast('Keep at least one tab', { icon: 'info' }); }
      afterTabsChanged();
    }
    if (el.dataset.set === 'pin') { S.pinned = el.checked; if (S.mode === 'panel') renderPanel(false); }
    if (el.dataset.set === 'hudVol') { S.hudVol = el.checked; store.set('hudVol', S.hudVol); }
    if (el.dataset.set === 'hudBright') { S.hudBright = el.checked; store.set('hudBright', S.hudBright); }
  });
  $('#settingsBody').addEventListener('input', e => {
    if (e.target.id !== 'setDelay') return;
    S.hoverDelay = +e.target.value; store.set('hoverDelay', S.hoverDelay);
    e.target.style.setProperty('--v', S.hoverDelay / 10 + '%');
    $('#setDelayV').textContent = S.hoverDelay + ' ms';
  });
  $('#settingsBody').addEventListener('click', e => {
    const b = e.target.closest('[data-set="unit"]');
    if (!b) return;
    S.unit = b.dataset.u; store.set('unit', S.unit);
    $$('[data-set="unit"]').forEach(x => x.classList.toggle('on', x === b));
    renderPeek(); refresh();
  });
  (() => {
    const bar = $('#settingsBar'), win = $('#settingsWin');
    let start = null;
    bar.addEventListener('pointerdown', e => {
      if (e.target.closest('button')) return;
      const r = win.getBoundingClientRect();
      start = { x: e.clientX - r.left, y: e.clientY - r.top };
      bar.setPointerCapture(e.pointerId);
    });
    bar.addEventListener('pointermove', e => {
      if (!start) return;
      win.style.transform = 'none';
      win.style.left = clamp(e.clientX - start.x, 0, innerWidth - 80) + 'px';
      win.style.top = clamp(e.clientY - start.y, 0, innerHeight - 40) + 'px';
    });
    bar.addEventListener('pointerup', () => { start = null; });
  })();

  // ---------- drops: real files, and the desktop's files ----------
  const isFileDrag = e => Array.from(e.dataTransfer?.types || []).includes('Files');
  notch.addEventListener('dragenter', e => { if (!isFileDrag(e)) return; e.preventDefault(); notch.classList.add('dropping'); if (S.tab !== 'tray' || S.mode !== 'panel') openTab('tray'); });
  notch.addEventListener('dragover', e => { if (isFileDrag(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
  notch.addEventListener('dragleave', e => { if (!notch.contains(e.relatedTarget)) notch.classList.remove('dropping'); });
  notch.addEventListener('drop', e => {
    e.preventDefault();
    notch.classList.remove('dropping');
    const files = Array.from(e.dataTransfer.files || []);
    files.forEach(f => {
      const ext = (f.name.split('.').pop() || 'FILE').slice(0, 4).toUpperCase();
      addToTray({ name: f.name, ext, c: '#64d2ff', thumb: f.type.startsWith('image/') ? URL.createObjectURL(f) : null }, true);
    });
    if (files.length) toast(`Shelved ${files.length} file${files.length === 1 ? '' : 's'} in the tray`, { icon: 'inbox' });
  });
  addEventListener('dragover', e => { if (isFileDrag(e)) e.preventDefault(); });
  addEventListener('drop', e => { if (isFileDrag(e)) e.preventDefault(); });

  function renderDesk() {
    $('#deskFiles').innerHTML = DESK.map(f => `<div class="dfile" data-id="${f.id}" role="button" tabindex="0" aria-label="${esc(f.name)}: drag onto the notch, or press Enter to shelve it">
      <span class="ftype" style="--c:${f.c};${f.thumb ? `background:${f.thumb}` : ''}"><span>${f.ext}</span></span><small>${esc(f.name)}</small></div>`).join('');
  }
  function shelveDesk(el) {
    const f = DESK.find(d => d.id === el.dataset.id);
    el.classList.add('gone');
    addToTray({ name: f.name, ext: f.ext, c: f.c, thumb: f.thumb, origin: f.id }, true);
    toast(`Shelved “${f.name}” in the tray`, { icon: 'inbox' });
  }
  function nearNotch(x, y, pad) {
    const r = notch.getBoundingClientRect();
    return x > r.left - pad && x < r.right + pad && y < r.bottom + pad;
  }
  $('#deskFiles').addEventListener('keydown', e => {
    const el = e.target.closest('.dfile');
    if (el && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); openTab('tray'); shelveDesk(el); }
  });
  $('#deskFiles').addEventListener('pointerdown', e => {
    const el = e.target.closest('.dfile');
    if (!el || el.classList.contains('gone')) return;
    e.preventDefault();
    const r = el.getBoundingClientRect();
    const ghost = el.cloneNode(true);
    ghost.classList.add('dragging');
    Object.assign(ghost.style, { left: r.left + 'px', top: r.top + 'px', width: r.width + 'px' });
    document.body.appendChild(ghost);
    const off = { x: e.clientX - r.left, y: e.clientY - r.top };
    el.style.opacity = '.3';
    dragActive = true;
    let moved = false;
    const move = ev => {
      moved = true;
      ghost.style.left = ev.clientX - off.x + 'px';
      ghost.style.top = ev.clientY - off.y + 'px';
      const near = nearNotch(ev.clientX, ev.clientY, 140);
      if (near && (S.mode !== 'panel' || S.tab !== 'tray')) openTab('tray');
      notch.classList.toggle('dropping', nearNotch(ev.clientX, ev.clientY, 30));
    };
    const up = ev => {
      removeEventListener('pointermove', move);
      removeEventListener('pointerup', up);
      dragActive = false;
      el.style.opacity = '';
      const hit = nearNotch(ev.clientX, ev.clientY, 30);
      notch.classList.remove('dropping');
      if (hit) {
        const nr = notch.getBoundingClientRect();
        ghost.animate([{ transform: 'scale(1.1) rotate(-4deg)' }, { transform: `translate(${nr.left + nr.width / 2 - ev.clientX}px, ${nr.top + 60 - ev.clientY}px) scale(.2)`, opacity: 0 }], { duration: 350, easing: 'cubic-bezier(.5,0,.75,0)' }).onfinish = () => ghost.remove();
        shelveDesk(el);
        return;
      }
      if (!moved) { ghost.remove(); return; }
      const back = el.getBoundingClientRect();
      ghost.animate([{ left: ghost.style.left, top: ghost.style.top }, { left: back.left + 'px', top: back.top + 'px' }], { duration: 400, easing: 'cubic-bezier(.22,1.18,.36,1)' }).onfinish = () => ghost.remove();
      ghost.style.left = back.left + 'px'; ghost.style.top = back.top + 'px';
    };
    addEventListener('pointermove', move);
    addEventListener('pointerup', up);
  });

  // copying text on the page feeds the Clipboard tab
  document.addEventListener('copy', e => {
    if (e.target.closest && e.target.closest('#notchWrap')) return;
    const sel = String(getSelection() || '').trim();
    if (sel) { addClip(sel); toast('Added to the Clipboard tab', { icon: 'clipboard-list' }); }
  });

  // ---------- page hooks into the notch ----------
  document.addEventListener('click', e => {
    const o = e.target.closest('[data-open-tab]');
    if (o) { e.preventDefault(); openTab(o.dataset.openTab); }
    const k = e.target.closest('[data-key]');
    if (k) pressKey(k.dataset.key);
  });

  // ---------- the one-second clock ----------
  function secondTick() {
    mediaTick();
    if (TM.running && !TM.paused) timerPaint();
    if (AW.end) awPaint();
    renderPeek();
    if (S.mode === 'panel' && S.tab === 'calendar' && new Date().getSeconds() === 0) refresh();
    const d = new Date();
    $('#mbClock').textContent = `${d.toLocaleDateString([], { weekday: 'short' })} ${clock(d)}`;
  }

  // ======================================================================
  //  SITE ANIMATIONS
  // ======================================================================
  function splitTitle() {
    const h = $('#heroTitle');
    let i = 0;
    const wrapWords = node => {
      const frag = document.createDocumentFragment();
      node.textContent.split(/(\s+)/).forEach(part => {
        if (!part) return;
        if (/^\s+$/.test(part)) { frag.appendChild(document.createTextNode(part)); return; }
        const s = document.createElement('span');
        s.className = 'w'; s.textContent = part; s.style.animationDelay = (0.35 + i++ * 0.09) + 's';
        frag.appendChild(s);
      });
      return frag;
    };
    Array.from(h.childNodes).forEach(n => {
      if (n.nodeType === 3) { h.replaceChild(wrapWords(n), n); return; }
      if (n.tagName === 'EM') { const s = document.createElement('span'); s.className = 'w'; s.style.animationDelay = (0.35 + i++ * 0.09 + .1) + 's'; h.replaceChild(s, n); s.appendChild(n); }
    });
  }

  function reveals() {
    const io = new IntersectionObserver(entries => entries.filter(en => en.isIntersecting).forEach(en => {
      const el = en.target;
      const sibs = Array.from(el.parentElement.children).filter(x => x.classList.contains('reveal'));
      el.style.animationDelay = Math.min(sibs.indexOf(el), 8) * 60 + 'ms';
      el.classList.add('in');
      io.unobserve(el);
    }), { threshold: .12 });
    $$('.reveal').forEach(el => io.observe(el));
  }

  function tilt() {
    if (reduced || matchMedia('(hover: none)').matches) return;
    $$('.tilt').forEach(c => {
      c.addEventListener('pointermove', e => {
        const r = c.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
        c.style.transform = `perspective(900px) rotateX(${(.5 - y) * 6}deg) rotateY(${(x - .5) * 8}deg) translateY(-4px)`;
        c.style.setProperty('--gx', x * 100 + '%'); c.style.setProperty('--gy', y * 100 + '%');
      });
      c.addEventListener('pointerleave', () => { c.style.transform = ''; });
    });
    $$('.magnetic').forEach(b => {
      b.addEventListener('pointermove', e => {
        const r = b.getBoundingClientRect();
        b.style.transform = `translate(${(e.clientX - r.left - r.width / 2) * .18}px, ${(e.clientY - r.top - r.height / 2) * .3}px)`;
      });
      b.addEventListener('pointerleave', () => { b.style.transform = ''; });
    });
  }

  function spotlight() {
    const hero = $('#hero');
    hero.addEventListener('pointermove', e => {
      const r = hero.getBoundingClientRect();
      hero.style.setProperty('--mx', e.clientX - r.left + 'px');
      hero.style.setProperty('--my', e.clientY - r.top + 'px');
    });
  }

  function progress() {
    const bar = $('#progress');
    const upd = () => { const h = document.documentElement.scrollHeight - innerHeight; bar.style.transform = `scaleX(${h > 0 ? scrollY / h : 0})`; };
    addEventListener('scroll', upd, { passive: true }); upd();
  }

  function ticker() {
    const chips = TABS.map(t => `<button class="chip" type="button" data-open-tab="${t.id}" style="--c:${t.c}"><span class="ci">${ic(t.icon)}</span>${t.name}</button>`).join('');
    $('#ticker').innerHTML = chips + chips.replace(/<button /g, '<button tabindex="-1" aria-hidden="true" ');
  }

  function counters() {
    const io = new IntersectionObserver(es => es.filter(e => e.isIntersecting).forEach(e => {
      const el = e.target, to = +el.dataset.to, t0 = performance.now();
      const step = t => { const p = Math.min(1, (t - t0) / 1200); el.textContent = Math.round(to * (1 - (1 - p) ** 3)); if (p < 1) requestAnimationFrame(step); };
      requestAnimationFrame(step);
      io.unobserve(el);
    }), { threshold: .6 });
    $$('.count').forEach(el => io.observe(el));
  }

  function timeline() {
    const poll = $('#tlPoll'), ev = $('#tlEvents');
    let visible = false;
    new IntersectionObserver(es => { visible = es[0].isIntersecting; }).observe($('.timeline'));
    const spawn = (track, cls, html, c) => {
      const m = document.createElement('span');
      m.className = 'tl-mark ' + cls;
      if (html) m.textContent = html;
      if (c) m.style.setProperty('--c', c);
      m.addEventListener('animationend', () => m.remove());
      track.appendChild(m);
    };
    setInterval(() => { if (visible && !document.hidden) spawn(poll, 'p'); }, 350);
    const EV = [['Track changed', '#ff375f'], ['Volume', '#2ec4b6'], ['Brightness', '#ff9f0a'], ['Paused', '#ff375f'], ['Timer done', '#ff9f0a'], ['Meeting soon', '#ff453a'], ['File dropped', '#64d2ff']];
    const next = () => setTimeout(() => {
      if (visible && !document.hidden) { const [l, c] = EV[Math.floor(Math.random() * EV.length)]; spawn(ev, 'e', l, c); }
      next();
    }, rnd(1600, 3800));
    next();
  }

  function gallery() {
    const SHOTS = [
      ['weather', 'Weather', 'The conditions now, the next twelve hours and the week, from where you are or any city you add.'],
      ['media', 'Now Playing', 'Apple Music and Spotify, on a card tinted with the artwork\'s colour.'],
      ['stats', 'System', 'CPU, GPU and memory rings, the last 24 seconds of CPU, and network and disk rates.'],
      ['processes', 'Processes', 'Your busiest processes. Sort by any column; the cross force quits after asking once.'],
      ['calculator', 'Calculator', 'Clicked, not typed, with × and ÷ before + and −, in decimals.'],
      ['timer', 'Timer', 'Presets from 1 to 45 minutes. The countdown shows in the peek.'],
      ['teleprompter', 'Teleprompter', 'Your script scrolls right under the lens.'],
      ['notes', 'Voice Notes', 'One click to record, from whichever microphone you pick.'],
      ['tray', 'File Tray', 'Drop files on the notch, then copy, AirDrop or reveal them.']
    ];
    const sec = $('#gallery'), tabs = $('#gTabs'), stage = $('#gStage'), cap = $('#gCap');
    tabs.innerHTML = SHOTS.map(([k, n], i) => `<button class="g-tab" type="button" role="tab" data-i="${i}">${n}</button>`).join('');
    stage.innerHTML = SHOTS.map(([k, n]) => `<img class="g-shot" src="assets/screens/${k}.png" alt="Notcho ${n} tab, screenshot" loading="lazy" width="640">`).join('');
    let cur = 0, timer = 0, visible = false;
    const show = i => {
      cur = (i + SHOTS.length) % SHOTS.length;
      $$('.g-tab', tabs).forEach((t, k) => { t.classList.toggle('on', k === cur); t.setAttribute('aria-selected', k === cur); });
      $$('.g-shot', stage).forEach((im, k) => im.classList.toggle('on', k === cur));
      cap.textContent = SHOTS[cur][2];
      clearTimeout(timer);
      if (visible && !sec.classList.contains('paused')) timer = setTimeout(() => show(cur + 1), 4500);
    };
    tabs.addEventListener('click', e => { const b = e.target.closest('.g-tab'); if (b) { sec.classList.add('paused'); show(+b.dataset.i); } });
    new IntersectionObserver(es => { visible = es[0].isIntersecting; show(cur); }, { threshold: .3 }).observe(sec);
    show(0);
  }

  // ======================================================================
  //  BOOT
  // ======================================================================
  // ======================================================================
  //  NACHO FIESTA: papel picado, drifting chips, and nacho rain from the hat
  // ======================================================================
  const FIESTA = ['#ff5a4e', '#ffc93c', '#8ac926', '#2ec4b6', '#ff7eb6', '#ff9f1c'];
  const CHIP_SVG = '<svg viewBox="0 0 40 40"><path d="M5 34 L20 4 L36 31 Z" fill="#f4b23e" stroke="#9c5a0c" stroke-width="2.6" stroke-linejoin="round"/>'
    + '<path d="M11 27 q4.5 6 9 0 q4 5.5 8.5 .5" fill="none" stroke="#ffd23f" stroke-width="4.5" stroke-linecap="round"/>'
    + '<circle cx="19" cy="17" r="1.6" fill="#b9701a" opacity=".6"/><circle cx="25" cy="22" r="1.2" fill="#b9701a" opacity=".6"/></svg>';
  const PEPPER_SVG = '<svg viewBox="0 0 40 40"><circle cx="20" cy="20" r="15" fill="#2e7d32"/><circle cx="20" cy="20" r="12" fill="#8bc34a"/>'
    + '<circle cx="20" cy="20" r="9" fill="#c5e1a5"/><g fill="#fff6c8"><circle cx="20" cy="14" r="1.8"/><circle cx="25" cy="18" r="1.8"/><circle cx="24" cy="24" r="1.8"/>'
    + '<circle cx="16" cy="24" r="1.8"/><circle cx="15" cy="18" r="1.8"/></g></svg>';
  const SALSA_SVG = '<svg viewBox="0 0 40 40"><rect x="9" y="9" width="22" height="22" rx="6" fill="#e53935"/><rect x="13" y="12" width="7" height="5" rx="2" fill="#ff8a80"/></svg>';

  // Papel picado strung either side of the notch, hanging along a sagging cord
  function bunting() {
    const SAG = 40;
    $('#bunting').innerHTML = ['l', 'r'].map(side =>
      `<div class="str ${side}"><svg class="cord" viewBox="0 0 100 100" preserveAspectRatio="none"><path d="M0 4 Q50 ${4 + 2 * SAG} 100 4" vector-effect="non-scaling-stroke"/></svg></div>`).join('');
    const place = () => $$('.bunting .str').forEach((str, si) => {
      const n = Math.max(2, Math.floor(str.clientWidth / 46)), h = str.clientHeight;
      const flags = Array.from({ length: n }, (_, i) => {
        const t = (i + .5) / n;
        const top = (4 + 2 * SAG * t * (1 - t)) / 100 * h - 2;
        return `<span class="flag" style="left:${t * 100}%;top:${top}px;--c:${FIESTA[(i + si * 3) % FIESTA.length]};animation-delay:${(-(i * .37 + si)).toFixed(2)}s"></span>`;
      }).join('');
      str.querySelectorAll('.flag').forEach(f => f.replaceWith());
      str.insertAdjacentHTML('beforeend', flags);
    });
    place();
    let t;
    addEventListener('resize', () => { clearTimeout(t); t = setTimeout(place, 150); });
  }

  function floaters() {
    const spots = [[6, 30], [14, 72], [86, 26], [92, 64], [24, 50], [76, 82], [52, 90], [68, 40]];
    $('#floaters').innerHTML = spots.map(([x, y], i) => {
      const size = 34 + (i * 13) % 30;
      const svg = i % 4 === 3 ? PEPPER_SVG : CHIP_SVG;
      return `<span class="nacho" style="left:${x}%;top:${y}%;width:${size}px;height:${size}px;--d:${16 + i * 3}s;--dl:-${i * 2}s;--dx:${(i % 2 ? -1 : 1) * (20 + i * 6)}px;--dy:${-(30 + i * 5)}px;--r0:${i * 40}deg;--r1:${i * 40 + 140}deg">${svg}</span>`;
    }).join('');
  }

  let raining = false;
  function nachoRain() {
    $('#hat img').animate([
      { transform: 'rotate(0) scale(1)' }, { transform: 'rotate(-18deg) scale(1.15)' },
      { transform: 'rotate(14deg) scale(1.1)' }, { transform: 'rotate(0) scale(1)' }
    ], { duration: 700, easing: 'ease-out' });
    const lines = ['Notcho Notcho Man!', 'We got all the fixins.', 'Extra cheese, coming up.', 'Hold the jalapeños? Never.'];
    toast(lines[Math.floor(Math.random() * lines.length)], { icon: 'party-popper' });
    if (reduced || raining) return;
    raining = true;
    setTimeout(() => { raining = false; }, 1200);

    const rain = $('#rain');
    const W = innerWidth, H = innerHeight;
    const count = Math.round(Math.min(70, Math.max(30, W / 22)));
    Array.from({ length: count }).forEach(() => {
      const el = document.createElement('span');
      const roll = Math.random();
      const size = 26 + Math.random() * 38;
      el.className = 'nacho';
      el.innerHTML = roll < .72 ? CHIP_SVG : roll < .88 ? PEPPER_SVG : SALSA_SVG;
      el.style.width = el.style.height = size + 'px';
      el.style.left = (Math.random() * (W + 80) - 40) + 'px';
      rain.appendChild(el);
      const drift = (Math.random() - .5) * 240, spin = (Math.random() - .5) * 900;
      el.animate([
        { transform: 'translate(0, 0) rotate(0deg)' },
        { transform: `translate(${drift}px, ${H + 160}px) rotate(${spin}deg)` }
      ], { duration: 1800 + Math.random() * 1800, delay: Math.random() * 900, easing: 'cubic-bezier(.35,.05,.6,1)', fill: 'both' })
        .finished.then(() => el.replaceWith());
    });
  }

  function fiesta() {
    bunting();
    floaters();
    $('#hat').addEventListener('click', nachoRain);
    $('#rainBtn').addEventListener('click', nachoRain);
  }

  function boot() {
    fillIcons();
    renderDesk();
    setBright(S.bright);
    renderPeek();
    setMode('peek');
    notch.classList.add('attract');
    loadWeather(0);
    setInterval(secondTick, 1000);
    secondTick();
    splitTitle();
    reveals();
    tilt();
    spotlight();
    progress();
    ticker();
    counters();
    timeline();
    gallery();
    fiesta();
  }
  if (window.lucide) boot();
  else addEventListener('load', boot);
})();
