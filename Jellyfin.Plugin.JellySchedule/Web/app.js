/* Jelly Schedule — web app (vanilla JS, no build step) */
(() => {
'use strict';

// ============================================================ utilities
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
const h = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const el = (html) => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; };
const pad2 = (n) => String(n).padStart(2, '0');
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const DAYS_SHORT = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const WEEK_ORDER = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const dayName = (d) => typeof d === 'number' ? DAYS[d] : String(d);
const dayShort = (d) => DAYS_SHORT[DAYS.indexOf(dayName(d))] || String(d);
const ICONS = {
  play: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>',
  pause: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>',
  rec: '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="7"/></svg>',
  check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>',
  cast: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M21 3H3c-1.1 0-2 .9-2 2v3h2V5h18v14h-7v2h7c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM1 18v3h3c0-1.66-1.34-3-3-3zm0-4v2c2.76 0 5 2.24 5 5h2c0-3.87-3.13-7-7-7zm0-4v2c4.97 0 9 4.03 9 9h2c0-6.08-4.93-11-11-11z"/></svg>',
  close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M18 6L6 18M6 6l12 12"/></svg>',
  plus: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>',
  trash: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14"/></svg>',
  up: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M18 15l-6-6-6 6"/></svg>',
  down: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M6 9l6 6 6-6"/></svg>',
  left: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M15 18l-6-6 6-6"/></svg>',
  right: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M9 18l6-6-6-6"/></svg>',
  cc: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M19 4H5a2 2 0 00-2 2v12a2 2 0 002 2h14a2 2 0 002-2V6a2 2 0 00-2-2zm-8 7H9.5v-.5h-2v3h2V13H11v1a1 1 0 01-1 1H7a1 1 0 01-1-1v-4a1 1 0 011-1h3a1 1 0 011 1v1zm7 0h-1.5v-.5h-2v3h2V13H18v1a1 1 0 01-1 1h-3a1 1 0 01-1-1v-4a1 1 0 011-1h3a1 1 0 011 1v1z"/></svg>',
  audio: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0014 7.97v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"/></svg>',
  full: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M8 3H3v5M16 3h5v5M8 21H3v-5M16 21h5v-5"/></svg>',
  tv: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="7" width="20" height="13" rx="2"/><path d="M17 2l-5 5-5-5"/></svg>',
  back10: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.99 5V1l-5 5 5 5V7c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6h-2c0 4.42 3.58 8 8 8s8-3.58 8-8-3.58-8-8-8z"/></svg>',
  fwd10: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 5V1l5 5-5 5V7c-3.31 0-6 2.69-6 6s2.69 6 6 6 6-2.69 6-6h2c0 4.42-3.58 8-8 8s-8-3.58-8-8 3.58-8 8-8z"/></svg>',
  refresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M21 12a9 9 0 11-3-6.7M21 3v6h-6"/></svg>',
  undo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 14L4 9l5-5"/><path d="M4 9h11a5 5 0 010 10h-2"/></svg>',
  pauseSmall: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>',
  edit: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 013 3L7 19l-4 1 1-4z"/></svg>'
};

// Times from the API carry the household time zone offset. Display the wall-clock part as-is.
function wall(iso) {
  if (!iso) return { date: '', hm: '', hour: 0, min: 0, dow: 0 };
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso);
  if (!m) return { date: '', hm: '', hour: 0, min: 0, dow: 0 };
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3]));
  return { date: `${m[1]}-${m[2]}-${m[3]}`, hm: `${m[4]}:${m[5]}`, hour: +m[4], min: +m[5], dow: d.getUTCDay(), day: +m[3], month: +m[2] - 1, year: +m[1] };
}
const ms = (iso) => iso ? new Date(iso).getTime() : 0;
const fmtDate = (isoDate) => { const [y, m, d] = isoDate.split('-').map(Number); const dt = new Date(Date.UTC(y, m - 1, d)); return `${DAYS_SHORT[dt.getUTCDay()]} ${d} ${MONTHS[m - 1]}`; };
const fmtDateLong = (isoDate) => { const [y, m, d] = isoDate.split('-').map(Number); const dt = new Date(Date.UTC(y, m - 1, d)); return `${DAYS[dt.getUTCDay()]} ${d} ${MONTHS[m - 1]}`; };
const addDays = (isoDate, n) => { const [y, m, d] = isoDate.split('-').map(Number); const dt = new Date(Date.UTC(y, m - 1, d + n)); return dt.toISOString().slice(0, 10); };
const dowOf = (isoDate) => { const [y, m, d] = isoDate.split('-').map(Number); return new Date(Date.UTC(y, m - 1, d)).getUTCDay(); };
const mondayOf = (isoDate) => addDays(isoDate, -((dowOf(isoDate) + 6) % 7));
const hmToMin = (hm) => { const [a, b] = String(hm || '0:0').split(':').map(Number); return a * 60 + b; };
const minToHm = (m) => `${pad2(Math.floor(((m % 1440) + 1440) % 1440 / 60))}:${pad2(((m % 60) + 60) % 60)}`;
const fmtDur = (min) => min >= 60 ? `${Math.floor(min / 60)}h${min % 60 ? ' ' + (min % 60) + 'm' : ''}` : `${min}m`;
const fmtTicks = (t) => { const s = Math.max(0, Math.floor((t || 0) / 1e7)); const hh = Math.floor(s / 3600), mm = Math.floor((s % 3600) / 60), ss = s % 60; return hh ? `${hh}:${pad2(mm)}:${pad2(ss)}` : `${mm}:${pad2(ss)}`; };
const fmtSec = (s) => fmtTicks(s * 1e7);
const epCode = (s, e) => (s == null && e == null) ? '' : `S${pad2(s || 0)}E${pad2(e || 0)}`;
const relDate = (iso, nowIso) => {
  if (!iso) return '';
  const w = wall(iso), n = wall(nowIso || new Date().toISOString());
  const diff = Math.round((Date.UTC(w.year, w.month, w.day) - Date.UTC(n.year, n.month, n.day)) / 864e5);
  if (diff === 0) return `today ${w.hm}`;
  if (diff === 1) return `tomorrow ${w.hm}`;
  if (diff === -1) return `yesterday ${w.hm}`;
  if (diff > 1 && diff < 7) return `${DAYS[w.dow]} ${w.hm}`;
  return `${DAYS_SHORT[w.dow]} ${w.day} ${MONTHS[w.month]}${w.hm ? ' ' + w.hm : ''}`;
};
const relDay = (iso) => {
  if (!iso) return '';
  const w = wall(iso);
  const diff = Math.round((Date.UTC(w.year, w.month, w.day) - Date.UTC(new Date().getFullYear(), new Date().getMonth(), new Date().getDate())) / 864e5);
  if (diff === 0) return 'today';
  if (diff === 1) return 'tomorrow';
  if (diff > 1 && diff < 7) return DAYS[w.dow];
  return `${w.day} ${MONTHS[w.month]}`;
};

function toast(msg, isErr) {
  const wrap = $('#toasts');
  const t = el(`<div class="toast ${isErr ? 'err' : ''}">${h(msg)}</div>`);
  wrap.appendChild(t);
  setTimeout(() => t.remove(), isErr ? 5000 : 2600);
}

function modal(html, opts = {}) {
  const bg = el(`<div class="modal-bg"><div class="modal ${opts.wide ? 'wide' : ''}"></div></div>`);
  const box = $('.modal', bg);
  box.innerHTML = html;
  const close = () => { bg.remove(); document.removeEventListener('keydown', onKey); opts.onClose && opts.onClose(); };
  const onKey = (e) => { if (e.key === 'Escape') close(); };
  document.addEventListener('keydown', onKey);
  bg.addEventListener('click', (e) => { if (e.target === bg && !opts.sticky) close(); });
  $$('[data-close]', box).forEach((b) => b.addEventListener('click', close));
  document.body.appendChild(bg);
  return { box, close };
}

function confirmDialog(title, text, okLabel = 'Confirm', danger = false) {
  return new Promise((resolve) => {
    const m = modal(`<div class="modal-head"><h2>${h(title)}</h2></div><p class="muted">${h(text)}</p>
      <div class="modal-foot"><button class="btn" data-close>Cancel</button><button class="btn ${danger ? 'danger' : 'primary'}" id="ok">${h(okLabel)}</button></div>`, { onClose: () => resolve(false) });
    $('#ok', m.box).addEventListener('click', () => { resolve(true); m.close(); });
  });
}

// ============================================================ API + auth
const base = location.pathname.replace(/\/JellySchedule\/app.*$/i, '');
const APP_VERSION = '1.0.0';
const store = {
  get(k, d) { try { const v = localStorage.getItem(k); return v == null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* ignore */ } },
  del(k) { try { localStorage.removeItem(k); } catch { /* ignore */ } }
};
let deviceId = store.get('jellyschedule.deviceId');
if (!deviceId) { deviceId = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2)).replace(/-/g, ''); store.set('jellyschedule.deviceId', deviceId); }
const deviceName = (() => { const ua = navigator.userAgent; if (/Android TV|SmartTV|Tizen|WebOS/i.test(ua)) return 'TV'; if (/iPhone|iPad/i.test(ua)) return 'iOS'; if (/Android/i.test(ua)) return 'Android'; if (/Macintosh/i.test(ua)) return 'Mac'; if (/Windows/i.test(ua)) return 'Windows'; if (/Linux/i.test(ua)) return 'Linux'; return 'Browser'; })();

const auth = { token: null, userId: null, userName: null, isAdmin: false, fromWeb: false };
const authHeader = () => `MediaBrowser Client="Jelly Schedule", Device="${deviceName}", DeviceId="${deviceId}", Version="${APP_VERSION}"${auth.token ? `, Token="${auth.token}"` : ''}`;

class ApiError extends Error { constructor(status, message, body) { super(message); this.status = status; this.body = body; } }

async function request(url, { method = 'GET', body, headers = {}, raw = false } = {}) {
  const init = { method, headers: { Authorization: authHeader(), ...headers } };
  if (body !== undefined) { init.headers['Content-Type'] = 'application/json'; init.body = JSON.stringify(body); }
  const res = await fetch(url, init);
  if (!res.ok) {
    let text = '';
    try { text = await res.text(); } catch { /* ignore */ }
    let msg = `${res.status} ${res.statusText}`;
    try { const j = JSON.parse(text); msg = j.Message || j.message || j.title || msg; } catch { if (text && text.length < 200) msg = text; }
    if (res.status === 401 && auth.token) { onUnauthorized(); }
    throw new ApiError(res.status, msg, text);
  }
  if (raw) return res;
  if (res.status === 204) return null;
  const ct = res.headers.get('content-type') || '';
  return ct.includes('json') ? res.json() : res.text();
}
const jf = (path, opts) => request(`${base}/${path}`, opts);
const js = (path, opts) => request(`${base}/JellySchedule/${path}`, opts);
const img = (id, type = 'Primary', w = 300) => `${base}/Items/${id}/Images/${type}?maxWidth=${w}&quality=88`;

function onUnauthorized() {
  auth.token = null; store.del('jellyschedule.auth');
  showLogin('Your session has expired. Please sign in again.');
}

async function tryRestoreAuth() {
  const saved = store.get('jellyschedule.auth');
  const candidates = [];
  if (saved && saved.token) candidates.push({ token: saved.token, fromWeb: false });
  try {
    const creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || 'null');
    (creds && creds.Servers || []).forEach((s) => { if (s.AccessToken) candidates.push({ token: s.AccessToken, fromWeb: true }); });
  } catch { /* ignore */ }
  for (const c of candidates) {
    auth.token = c.token;
    try {
      const me = await jf('Users/Me');
      auth.userId = me.Id; auth.userName = me.Name; auth.isAdmin = !!(me.Policy && me.Policy.IsAdministrator); auth.fromWeb = c.fromWeb;
      if (!c.fromWeb) store.set('jellyschedule.auth', { token: c.token });
      return true;
    } catch { auth.token = null; }
  }
  return false;
}

async function login(username, password) {
  const r = await jf('Users/AuthenticateByName', { method: 'POST', body: { Username: username, Pw: password || '' } });
  auth.token = r.AccessToken; auth.userId = r.User.Id; auth.userName = r.User.Name; auth.isAdmin = !!(r.User.Policy && r.User.Policy.IsAdministrator); auth.fromWeb = false;
  store.set('jellyschedule.auth', { token: r.AccessToken });
}

async function logout() {
  try { await jf('Sessions/Logout', { method: 'POST' }); } catch { /* ignore */ }
  store.del('jellyschedule.auth'); auth.token = null;
  location.reload();
}

// ============================================================ app state
const state = { st: null, guide: null, lineup: null, recordings: null, weekStart: null, selectedDay: null, nowTimer: null, clockOffset: 0 };
const settings = () => (state.st && state.st.Settings) || {};
const canEdit = () => !!(state.st && state.st.CanEdit);
const viewingDays = () => { const s = new Set(); (state.st?.Windows || []).forEach((w) => (w.Days || []).forEach((d) => s.add(dayName(d)))); return WEEK_ORDER.filter((d) => s.has(d)); };
const householdMismatch = () => state.st && state.st.HouseholdUser && state.st.Me && state.st.HouseholdUser.Id !== state.st.Me.Id;
const serverNow = () => new Date(Date.now() + state.clockOffset).toISOString();

async function loadState() {
  state.st = await js('state');
  state.clockOffset = ms(state.st.Now) - Date.now();
  renderTopbar();
}

// ============================================================ shell + router
function renderTopbar() {
  const top = $('#topbar');
  const me = state.st?.Me || { Name: auth.userName || '' };
  top.innerHTML = `
    <div class="brand"><div class="logo">${ICONS.tv}</div><span class="text">Jelly Schedule</span></div>
    <nav class="nav">
      <a href="#guide" data-view="guide">Guide</a>
      <a href="#lineup" data-view="lineup">Lineup</a>
      <a href="#schedule" data-view="schedule">Schedule</a>
      <a href="#recordings" data-view="recordings">Recordings</a>
      <a href="#settings" data-view="settings">Settings</a>
    </nav>
    <div class="userchip">
      <span id="onair-slot"></span>
      <span class="avatar" title="${h(me.Name)}">${h((me.Name || '?').slice(0, 1).toUpperCase())}</span>
      <span class="nowrap">${h(me.Name)}</span>
      ${auth.fromWeb ? '' : '<button class="btn sm ghost" id="logout">Sign out</button>'}
    </div>`;
  const lo = $('#logout', top); if (lo) lo.addEventListener('click', logout);
  highlightNav();
}
function highlightNav() { const v = currentView(); $$('.nav a').forEach((a) => a.classList.toggle('active', a.dataset.view === v)); }
function currentView() { const v = (location.hash || '#guide').slice(1).split('?')[0]; return ['guide', 'lineup', 'schedule', 'recordings', 'settings'].includes(v) ? v : 'guide'; }

async function route() {
  highlightNav();
  const main = $('#main');
  main.innerHTML = '<div class="loading"><span class="spinner"></span> Loading…</div>';
  try {
    switch (currentView()) {
      case 'lineup': await renderLineup(main); break;
      case 'schedule': await renderSchedule(main); break;
      case 'recordings': await renderRecordings(main); break;
      case 'settings': await renderSettings(main); break;
      default: await renderGuide(main);
    }
  } catch (e) {
    console.error(e);
    main.innerHTML = `<div class="empty"><h3>Something went wrong</h3><p>${h(e.message || e)}</p><button class="btn" onclick="location.reload()">Reload</button></div>`;
  }
}
window.addEventListener('hashchange', route);

function showLogin(message) {
  $('#topbar').innerHTML = '';
  $('#main').innerHTML = `<div class="login"><div class="card">
    <div class="brand"><div class="logo">${ICONS.tv}</div><span>Jelly Schedule</span></div>
    <p class="muted center small">Sign in with your Jellyfin account</p>
    ${message ? `<p class="error center">${h(message)}</p>` : ''}
    <form id="loginForm">
      <div class="field"><label>Username</label><input type="text" id="u" autocomplete="username" autofocus></div>
      <div class="field"><label>Password</label><input type="password" id="p" autocomplete="current-password"></div>
      <p class="error" id="loginErr"></p>
      <button class="btn primary lg" style="width:100%;justify-content:center" type="submit">Sign in</button>
    </form>
  </div></div>`;
  $('#loginForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    try { await login($('#u').value.trim(), $('#p').value); await boot(); }
    catch (err) { $('#loginErr').textContent = err.status === 401 ? 'Wrong username or password.' : (err.message || 'Login failed'); }
  });
}

// ============================================================ guide
function airingTitle(a) { return a.Kind === 'Movie' || !a.SeriesName ? a.Title : a.SeriesName; }
function airingSub(a) {
  if (a.Kind === 'Movie') return [a.Year, a.OfficialRating, a.RuntimeMinutes ? fmtDur(a.RuntimeMinutes) : null].filter(Boolean).join(' · ');
  const code = epCode(a.Season, a.Episode);
  return [code, a.Title].filter(Boolean).join(' · ');
}
function airingPoster(a, w = 200) {
  if (a.Kind !== 'Movie' && a.SeriesId && a.SeriesHasPrimaryImage) return img(a.SeriesId, 'Primary', w);
  if (a.HasPrimaryImage) return img(a.ItemId, 'Primary', w);
  return null;
}
function airingBackdrop(a) {
  if (!a.HasBackdrop) return null;
  return img(a.Kind === 'Movie' || !a.SeriesId ? a.ItemId : a.SeriesId, 'Backdrop', 1280);
}
function airingBadges(a, compact, skipLive) {
  const b = [];
  if (a.IsOnNow && !skipLive) b.push('<span class="badge live">On now</span>');
  if (a.Kind === 'Movie') b.push('<span class="badge movie">Movie</span>');
  if (a.Kind === 'ReRun') b.push('<span class="badge rerun">Re-run</span>');
  if (a.Kind === 'OneOff') b.push('<span class="badge oneoff">One-off</span>');
  if (a.IsSeriesFinale) b.push('<span class="badge finale">Series finale</span>');
  else if (a.IsSeasonFinale) b.push('<span class="badge finale">Season finale</span>');
  else if (a.IsSeasonPremiere) b.push('<span class="badge new">Premiere</span>');
  if (a.IsRecorded) b.push('<span class="badge rec">Rec</span>');
  if (a.IsWatched) b.push('<span class="badge ok">Watched</span>');
  else if (a.IsMissed) b.push('<span class="badge warn">Missed</span>');
  else if (a.PositionTicks > 0 && !compact) b.push('<span class="badge">In progress</span>');
  if (a.RunsOver && !compact) b.push('<span class="badge">Runs late</span>');
  return b.join('');
}
function posterHtml(url, cls, label) {
  return `<div class="poster ${cls}">${url ? `<img src="${url}" alt="" loading="lazy" onerror="this.remove()">` : ''}<div class="ph">${h(label || '')}</div></div>`;
}

async function loadGuide(weekStart) {
  state.guide = await js(`guide?from=${weekStart}&days=7`);
  state.clockOffset = ms(state.guide.Now) - Date.now();
  return state.guide;
}

async function renderGuide(main) {
  if (!state.weekStart) state.weekStart = mondayOf(wall(serverNow()).date);
  const g = await loadGuide(state.weekStart);
  const today = wall(g.Now).date;
  if (!state.selectedDay || !g.Days.some((d) => d.Date === state.selectedDay)) state.selectedDay = g.Days.some((d) => d.Date === today) ? today : g.Days[0].Date;

  main.innerHTML = '';
  if (g.Warning === 'empty') {
    main.appendChild(el(`<div class="empty"><h3>Welcome to Jelly Schedule</h3>
      <p>Your channel is empty. First choose <b>when</b> you watch, then <b>what</b> you watch.</p>
      <div class="row" style="justify-content:center;gap:8px;margin-top:10px"><a class="btn primary" href="#schedule">1. Set viewing times</a><a class="btn" href="#lineup">2. Build the lineup</a></div></div>`));
    return;
  }
  if (householdMismatch()) {
    main.appendChild(el(`<div class="warnbar">You are signed in as <b>${h(state.st.Me.Name)}</b>, but this schedule follows <b>${h(state.st.HouseholdUser.Name)}</b>'s watch history. Anything you watch here is also marked as watched for ${h(state.st.HouseholdUser.Name)}.</div>`));
  }
  if (g.Warning && g.Warning !== 'empty') main.appendChild(el(`<div class="warnbar">${h(g.Warning)}</div>`));

  main.appendChild(renderHero(g));
  main.appendChild(renderWeek(g));
  startNowPolling();
}

function renderHero(g) {
  const wrap = el('<div class="hero"></div>');
  const a = g.OnNow;
  if (a) {
    const bd = airingBackdrop(a);
    const pct = a.RuntimeMinutes ? Math.min(100, Math.max(0, (ms(serverNow()) - ms(a.Start)) / (a.RuntimeMinutes * 60000) * 100)) : 0;
    const live = settings().LiveMode === 'Strict';
    wrap.appendChild(el(`<div class="hero-card">
      <div class="bg" style="${bd ? `background-image:url('${bd}')` : ''}"></div>
      <div class="inner">
        ${posterHtml(airingPoster(a, 300), 'p-lg', airingTitle(a))}
        <div class="meta">
          <div class="row wrap"><span class="onair-pill"><span class="dot"></span> ON NOW</span> <span class="muted small">${wall(a.Start).hm}–${wall(a.End).hm}</span> ${airingBadges(a, true, true)}</div>
          <h2>${h(airingTitle(a))}</h2>
          <div class="sub">${h(airingSub(a))}</div>
          <div class="ov">${h(a.Overview || '')}</div>
          <div class="prog"><div class="progress"><i style="width:${pct.toFixed(1)}%"></i></div><span>${live ? 'joined in progress' : 'starts from the beginning'}</span></div>
          <div class="actions">
            <button class="btn primary lg" id="tuneIn">${ICONS.play} Tune in</button>
            <button class="btn" id="heroDetails">Details</button>
            ${a.IsRecorded ? '' : `<button class="btn" id="heroRecord" title="Watch it later instead">${ICONS.rec} Record</button>`}
            <button class="btn" id="heroCast" title="Play on another device">${ICONS.cast}</button>
          </div>
        </div>
      </div></div>`));
    $('#tuneIn', wrap).addEventListener('click', () => Player.playAiring(a, { live: true }));
    $('#heroDetails', wrap).addEventListener('click', () => showAiring(a));
    const rec = $('#heroRecord', wrap); if (rec) rec.addEventListener('click', () => recordAiring(a));
    $('#heroCast', wrap).addEventListener('click', () => castDialog(a));
  } else {
    const next = g.UpNext;
    wrap.appendChild(el(`<div class="hero-card offair"><div class="inner">
      <div class="tv-static">Off air</div>
      ${next ? `<div class="sub">Next up: <b>${h(airingTitle(next))}</b> <span class="muted">${h(airingSub(next))}</span></div><div class="muted">${h(relDate(next.Start, g.Now))}</div>` : `<div class="muted">Nothing is scheduled yet.</div>`}
      <div class="actions">${next ? `<button class="btn" id="heroNext">Details</button><button class="btn ghost" id="heroEarly" title="Watch it now anyway">${ICONS.play} Watch early</button>` : ''}</div>
    </div></div>`));
    const b = $('#heroNext', wrap); if (b) b.addEventListener('click', () => showAiring(next));
    const e = $('#heroEarly', wrap); if (e) e.addEventListener('click', () => Player.playAiring(next, { live: false }));
  }

  // Up next list
  const flat = g.Days.flatMap((d) => d.Airings).filter((x) => ms(x.Start) > ms(serverNow()));
  const upcoming = (g.UpNext && !flat.some((x) => x.Id === g.UpNext.Id) ? [g.UpNext] : []).concat(flat).slice(0, 5);
  const box = el(`<div class="card"><h3>Up next</h3><div class="upnext-list"></div>
    <div class="stats" style="margin-top:12px">
      <span><b>${g.Stats.Programmes}</b> programmes</span><span><b>${fmtDur(g.Stats.ScheduledMinutes)}</b> this week</span>
      <span><b>${g.Stats.Episodes}</b> episodes</span><span><b>${g.Stats.Movies}</b> movie${g.Stats.Movies === 1 ? '' : 's'}</span>
      ${g.Stats.Missed ? `<span style="color:var(--warn)"><b>${g.Stats.Missed}</b> missed</span>` : ''}
    </div></div>`);
  const list = $('.upnext-list', box);
  if (!upcoming.length) list.innerHTML = '<div class="muted small">Nothing more this week.</div>';
  upcoming.forEach((x) => {
    const r = el(`<div class="upnext"><span class="when">${h(relDate(x.Start, g.Now))}</span>${posterHtml(airingPoster(x, 100), 'p-sm', '')}<div class="grow" style="min-width:0"><div class="t">${h(airingTitle(x))}</div><div class="s">${h(airingSub(x))}</div></div><span class="bd">${airingBadges(x, true)}</span></div>`);
    r.addEventListener('click', () => showAiring(x));
    list.appendChild(r);
  });
  if (g.ComingUp && g.ComingUp.length) {
    const cs = el(`<div style="margin-top:12px"><div class="muted small" style="margin-bottom:6px">Airing on TV this week (not in your library yet)</div><div class="comingsoon"></div></div>`);
    g.ComingUp.slice(0, 8).forEach((c) => $('.comingsoon', cs).appendChild(el(`<div class="cs"><div class="d">${h(relDate(c.AirDate, g.Now))}${c.Network ? ' · ' + h(c.Network) : ''}</div><div class="t">${h(c.SeriesName)}</div><div class="muted">${epCode(c.Season, c.Episode)} ${h(c.Title)}</div></div>`)));
    box.appendChild(cs);
  }
  wrap.appendChild(box);
  return wrap;
}

function renderWeek(g) {
  const wrap = el('<div class="section"></div>');
  const ws = state.weekStart;
  const nav = el(`<div class="week-nav">
    <button class="btn icon" id="prevWeek" title="Previous week">${ICONS.left}</button>
    <button class="btn icon" id="nextWeek" title="Next week">${ICONS.right}</button>
    <button class="btn sm" id="thisWeek">This week</button>
    <h2>${fmtDate(ws)} – ${fmtDate(addDays(ws, 6))}</h2>
    <span class="right row">${canEdit() ? `<button class="btn sm" id="addOneOff">${ICONS.plus} One-off</button>` : ''}<button class="btn sm ghost" id="refreshGuide" title="Refresh">${ICONS.refresh}</button></span>
  </div>`);
  $('#prevWeek', nav).addEventListener('click', () => { state.weekStart = addDays(ws, -7); state.selectedDay = null; route(); });
  $('#nextWeek', nav).addEventListener('click', () => { state.weekStart = addDays(ws, 7); state.selectedDay = null; route(); });
  $('#thisWeek', nav).addEventListener('click', () => { state.weekStart = mondayOf(wall(serverNow()).date); state.selectedDay = null; route(); });
  $('#refreshGuide', nav).addEventListener('click', route);
  const oo = $('#addOneOff', nav); if (oo) oo.addEventListener('click', () => oneOffDialog());
  wrap.appendChild(nav);

  const isNarrow = window.innerWidth < 900;
  wrap.appendChild(isNarrow ? renderDayList(g) : renderGrid(g));
  return wrap;
}

function gridRange(g) {
  // rows from earliest window start to latest end (in minutes from midnight; end can exceed 1440)
  let start = 24 * 60, end = 0;
  g.Days.forEach((d) => {
    d.Windows.forEach((w) => { const s = wall(w.Start), e = wall(w.End); const sm = s.hour * 60 + s.min; let em = e.hour * 60 + e.min; if (e.date !== s.date) em += 1440; start = Math.min(start, sm); end = Math.max(end, em); });
    d.Airings.forEach((a) => { const s = wall(a.Start), e = wall(a.End); const sm = s.hour * 60 + s.min; let em = e.hour * 60 + e.min; if (e.date !== s.date) em += 1440; if (e.date === s.date || em > sm) { start = Math.min(start, sm); end = Math.max(end, em); } });
  });
  if (start >= end) { start = 18 * 60; end = 23 * 60; }
  start = Math.floor(start / 30) * 30; end = Math.ceil(end / 30) * 30;
  return { start, end };
}

function minutesFromDay(day, iso) { const w = wall(iso); let m = w.hour * 60 + w.min; if (w.date !== day) m += w.date > day ? 1440 : -1440; return m; }

function renderGrid(g) {
  const { start, end } = gridRange(g);
  const rowMin = 30; const rowPx = 44; const rows = (end - start) / rowMin;
  const wrap = el('<div class="guide"><div class="guide-scroll"><div class="ggrid"></div></div></div>');
  const grid = $('.ggrid', wrap);
  grid.appendChild(el('<div class="gtime corner"></div>'));
  g.Days.forEach((d) => grid.appendChild(el(`<div class="ghead ${d.IsToday ? 'today' : ''} ${d.Blackout ? 'blackout' : ''}"><div>${DAYS_SHORT[dowOf(d.Date)]}</div><div class="d">${fmtDate(d.Date).slice(4)}</div></div>`)));
  // time column
  const timeCol = el(`<div class="gtime" style="height:${rows * rowPx}px;position:relative"></div>`);
  for (let i = 0; i < rows; i++) { const m = start + i * rowMin; if (m % 60 === 0) timeCol.appendChild(el(`<div style="position:absolute;top:${i * rowPx + 3}px;right:8px">${minToHm(m)}</div>`)); }
  timeCol.classList.remove('gtime'); timeCol.className = 'gtime'; timeCol.style.position = 'sticky';
  grid.appendChild(timeCol);
  const nowIso = serverNow(); const nowDay = wall(nowIso).date;
  g.Days.forEach((d) => {
    const col = el(`<div class="gcol ${d.IsToday ? 'today' : ''} ${d.Blackout ? 'blackout' : ''}" style="height:${rows * rowPx}px"></div>`);
    for (let i = 0; i < rows; i++) col.appendChild(el(`<div class="grow-line" style="top:${i * rowPx}px"></div>`));
    d.Windows.forEach((w) => {
      const s = minutesFromDay(d.Date, w.Start), e = minutesFromDay(d.Date, w.End);
      col.appendChild(el(`<div class="win" style="top:${(s - start) / rowMin * rowPx}px;height:${(e - s) / rowMin * rowPx - 2}px">${w.Label ? `<span class="lbl">${h(w.Label)}</span>` : ''}</div>`));
      // empty gaps inside the window
      if (canEdit() && !d.IsPast) {
        let cursor = s;
        const inWin = d.Airings.map((a) => ({ s: minutesFromDay(d.Date, a.Start), e: minutesFromDay(d.Date, a.End) })).filter((x) => x.e > s && x.s < e).sort((x, y) => x.s - y.s);
        inWin.concat([{ s: e, e }]).forEach((x) => { if (x.s - cursor >= rowMin) { const gs = cursor, ge = x.s; const gap = el(`<div class="gap" style="top:${(gs - start) / rowMin * rowPx + 2}px;height:${(ge - gs) / rowMin * rowPx - 4}px" title="Schedule something here">${(ge - gs) >= 60 ? 'off air · +' : '+'}</div>`); gap.addEventListener('click', () => oneOffDialog(d.Date, minToHm(gs))); col.appendChild(gap); } cursor = Math.max(cursor, x.e); });
      }
    });
    if (d.Blackout) col.appendChild(el(`<div class="away">${h(d.Blackout.Label)}</div>`));
    d.Airings.forEach((a) => {
      const s = minutesFromDay(d.Date, a.Start), e = minutesFromDay(d.Date, a.End);
      const top = (s - start) / rowMin * rowPx, height = (e - s) / rowMin * rowPx;
      const cls = ['prog-block', a.Kind === 'Movie' ? 'movie' : a.Kind === 'ReRun' ? 'rerun' : a.Kind === 'OneOff' ? 'oneoff' : '', a.IsWatched ? 'watched' : '', a.IsMissed ? 'missed' : '', a.IsOnNow ? 'onnow' : '', ms(a.End) < ms(nowIso) ? 'past' : '', height < 60 ? 'short' : ''].join(' ');
      const p = airingPoster(a, 100);
      const b = el(`<div class="${cls}" style="top:${top + 2}px;height:${height - 4}px">
        <div class="pi">${p ? `<img src="${p}" alt="" loading="lazy" onerror="this.remove()">` : ''}</div>
        <div class="txt"><div class="tm">${wall(a.Start).hm}</div><div class="t">${h(airingTitle(a))}</div><div class="s">${h(airingSub(a))}</div><div class="bd">${airingBadges(a, true)}</div></div></div>`);
      b.addEventListener('click', () => showAiring(a));
      col.appendChild(b);
    });
    if (d.Date === nowDay) {
      const nm = minutesFromDay(d.Date, nowIso);
      if (nm >= start && nm <= end) col.appendChild(el(`<div class="nowline" style="top:${(nm - start) / rowMin * rowPx}px"></div>`));
    }
    grid.appendChild(col);
  });
  // scroll to now
  setTimeout(() => { const sc = $('.guide-scroll', wrap); const nm = minutesFromDay(nowDay, nowIso); if (nm > start) sc.scrollTop = Math.max(0, (nm - start) / rowMin * rowPx - 120); }, 0);
  return wrap;
}

function renderDayList(g) {
  const wrap = el('<div></div>');
  const tabs = el('<div class="daytabs"></div>');
  g.Days.forEach((d) => { const t = el(`<div class="daytab ${d.Date === state.selectedDay ? 'on' : ''}"><div>${DAYS_SHORT[dowOf(d.Date)]}</div><div class="d">${fmtDate(d.Date).slice(4)}</div></div>`); t.addEventListener('click', () => { state.selectedDay = d.Date; route(); }); tabs.appendChild(t); });
  wrap.appendChild(tabs);
  const d = g.Days.find((x) => x.Date === state.selectedDay) || g.Days[0];
  const list = el('<div class="daylist"></div>');
  if (d.Blackout) list.appendChild(el(`<div class="empty">${h(d.Blackout.Label)} — nothing scheduled.</div>`));
  else if (!d.Airings.length) list.appendChild(el(`<div class="empty">Off air${d.Windows.length ? ' — nothing left to schedule in this window.' : '.'}${canEdit() && !d.IsPast ? ' <button class="btn sm" id="dayOneOff">Schedule a one-off</button>' : ''}</div>`));
  d.Airings.forEach((a) => {
    const r = el(`<div class="dayrow ${a.Kind === 'Movie' ? 'movie' : a.Kind === 'ReRun' ? 'rerun' : a.Kind === 'OneOff' ? 'oneoff' : ''} ${a.IsOnNow ? 'onnow' : ''} ${a.IsWatched ? 'watched' : ''}"><span class="when">${wall(a.Start).hm}</span>${posterHtml(airingPoster(a, 100), 'p-sm', '')}<div class="grow" style="min-width:0"><div class="t">${h(airingTitle(a))}</div><div class="s">${h(airingSub(a))}</div><div class="row" style="gap:4px;margin-top:3px">${airingBadges(a, true)}</div></div>${ICONS.right}</div>`);
    r.addEventListener('click', () => showAiring(a));
    list.appendChild(r);
  });
  wrap.appendChild(list);
  const oo = $('#dayOneOff', list); if (oo) oo.addEventListener('click', () => oneOffDialog(d.Date));
  return wrap;
}

// ---------------------------------------------------------------- airing detail
function showAiring(a) {
  const isLive = a.IsOnNow;
  const past = ms(a.End) < ms(serverNow());
  const bd = airingBackdrop(a);
  const m = modal(`<div class="modal-head"><h2>${h(airingTitle(a))}</h2><span class="right row">${airingBadges(a)}</span><button class="btn icon ghost" data-close>${ICONS.close}</button></div>
    <div class="detail">
      ${posterHtml(airingPoster(a, 300), '', airingTitle(a))}
      <div class="meta">
        <div class="sub muted">${h(airingSub(a))}</div>
        <div class="kv" style="margin-top:8px">
          <b>When</b><span>${h(relDate(a.Start, serverNow()))} – ${wall(a.End).hm}${a.RunsOver ? ' (runs past the viewing window)' : ''}</span>
          <b>Runtime</b><span>${fmtDur(a.RuntimeMinutes)} in a ${fmtDur(a.DurationMinutes)} slot</span>
          ${a.Year && a.Kind !== 'Movie' ? `<b>Year</b><span>${a.Year}</span>` : ''}
          ${a.CommunityRating ? `<b>Rating</b><span>★ ${a.CommunityRating.toFixed(1)}</span>` : ''}
          ${a.PositionTicks ? `<b>Progress</b><span>${fmtTicks(a.PositionTicks)} watched</span>` : ''}
        </div>
        <div class="ov">${h(a.Overview || 'No description.')}</div>
        <div class="actions">
          <button class="btn primary" id="dPlay">${ICONS.play} ${isLive ? 'Tune in' : past ? 'Watch' : 'Watch now'}</button>
          ${a.PositionTicks > 0 ? `<button class="btn" id="dResume">Resume ${fmtTicks(a.PositionTicks)}</button>` : ''}
          <button class="btn" id="dCast" title="Play on another device">${ICONS.cast} Play on…</button>
          ${canEdit() && a.Kind !== 'ReRun' ? (a.IsRecorded ? `<button class="btn" id="dUnrecord">${ICONS.undo} Cancel recording</button>` : `<button class="btn" id="dRecord" title="Keep it for later; the schedule moves on">${ICONS.rec} Record for later</button>`) : ''}
          ${canEdit() ? (a.IsWatched ? `<button class="btn ghost" id="dUnwatched">Mark unwatched</button>` : `<button class="btn ghost" id="dWatched">${ICONS.check} Mark watched</button>`) : ''}
          ${a.Kind === 'OneOff' && canEdit() ? `<button class="btn danger" id="dRemoveOneOff">${ICONS.trash} Remove</button>` : ''}
        </div>
        <p class="small muted" style="margin-top:12px">${a.Kind === 'ReRun' ? 'Re-runs ignore watched status.' : a.IsRecorded ? 'Recorded: this episode is waiting in Recordings and the show carries on from the next one.' : a.IsMissed ? 'You missed this one. It will air again in the next slot for this show, or record it to watch whenever you like.' : 'If you miss it, it simply airs again next time. Record it to keep the show moving.'}</p>
      </div>
    </div>`);
  $('#dPlay', m.box).addEventListener('click', () => { m.close(); Player.playAiring(a, { live: isLive }); });
  const r = $('#dResume', m.box); if (r) r.addEventListener('click', () => { m.close(); Player.playAiring(a, { live: false, resume: true }); });
  $('#dCast', m.box).addEventListener('click', () => { m.close(); castDialog(a); });
  const rec = $('#dRecord', m.box); if (rec) rec.addEventListener('click', async () => { m.close(); await recordAiring(a); });
  const unrec = $('#dUnrecord', m.box); if (unrec) unrec.addEventListener('click', async () => { m.close(); await js(`recordings/${a.RecordingId}`, { method: 'DELETE' }); toast('Recording cancelled'); route(); });
  const w = $('#dWatched', m.box); if (w) w.addEventListener('click', async () => { m.close(); await setWatched(a.ItemId, true); route(); });
  const uw = $('#dUnwatched', m.box); if (uw) uw.addEventListener('click', async () => { m.close(); await setWatched(a.ItemId, false); route(); });
  const ro = $('#dRemoveOneOff', m.box); if (ro) ro.addEventListener('click', async () => {
    const one = (state.st.OneOffs || []).find((o) => o.ItemId === a.ItemId && o.Date === wall(a.Start).date);
    m.close(); if (one) { await js(`one-offs/${one.Id}`, { method: 'DELETE' }); await loadState(); toast('One-off removed'); route(); }
  });
}

async function recordAiring(a) {
  try {
    await js('recordings', { method: 'POST', body: { ItemId: a.ItemId, AiringStart: a.Start, LineupEntryId: a.LineupEntryId } });
    toast(`Recorded — find it under Recordings`);
    route();
  } catch (e) { toast(e.message, true); }
}

async function setWatched(itemId, played) {
  try {
    await js('playstate', { method: 'POST', body: { ItemId: itemId, Played: played } });
    toast(played ? 'Marked as watched' : 'Marked as unwatched');
  } catch (e) { toast(e.message, true); }
}

async function castDialog(a) {
  const m = modal(`<div class="modal-head"><h2>Play on…</h2><button class="btn icon ghost" data-close>${ICONS.close}</button></div><div class="loading"><span class="spinner"></span> Looking for devices…</div>`);
  try {
    const sessions = await jf(`Sessions?controllableByUserId=${auth.userId}`);
    const list = (sessions || []).filter((s) => s.DeviceId !== deviceId && s.SupportsRemoteControl !== false && s.Capabilities && s.Capabilities.PlayableMediaTypes && s.Capabilities.PlayableMediaTypes.includes('Video'));
    const box = el('<div class="list"></div>');
    if (!list.length) box.innerHTML = '<div class="empty">No other devices are online right now.<br><span class="small">Open Jellyfin on your TV or phone first, then try again.</span></div>';
    list.forEach((s) => {
      const r = el(`<div class="item-row" style="cursor:pointer"><div class="grow"><div class="title">${h(s.DeviceName || s.Client)}</div><div class="sub">${h(s.Client)} · ${h(s.UserName || '')}${s.NowPlayingItem ? ' · playing ' + h(s.NowPlayingItem.Name) : ''}</div></div>${ICONS.play}</div>`);
      r.addEventListener('click', async () => {
        try {
          const ticks = settings().LiveMode === 'Strict' && a.IsOnNow ? Math.max(0, ms(serverNow()) - ms(a.Start)) * 1e4 : (a.PositionTicks || 0);
          await jf(`Sessions/${encodeURIComponent(s.Id)}/Playing?playCommand=PlayNow&itemIds=${a.ItemId}&startPositionTicks=${ticks}`, { method: 'POST' });
          toast(`Playing on ${s.DeviceName || s.Client}`); m.close();
        } catch (e) { toast(e.message, true); }
      });
      box.appendChild(r);
    });
    $('.loading', m.box).replaceWith(box);
  } catch (e) { $('.loading', m.box).outerHTML = `<div class="empty">${h(e.message)}</div>`; }
}

// ---------------------------------------------------------------- one-off dialog
function oneOffDialog(date, start) {
  const today = wall(serverNow()).date;
  let selected = null;
  const m = modal(`<div class="modal-head"><h2>Schedule a one-off</h2><button class="btn icon ghost" data-close>${ICONS.close}</button></div>
    <p class="muted small">Put a specific movie or episode on the schedule for one evening — a birthday film, a season premiere party, anything.</p>
    <div class="split">
      <div class="field"><label>Date</label><input type="date" id="ooDate" value="${date || today}" min="${today}"></div>
      <div class="field"><label>Start</label><input type="time" id="ooTime" value="${start || (state.st.Windows[0]?.Start || '20:00')}" step="900"></div>
    </div>
    <div class="field"><label>What</label><input type="search" id="ooQ" placeholder="Search movies and shows…" autofocus></div>
    <div id="ooResults" class="results"></div>
    <div id="ooEpisodes" class="hidden" style="margin-top:10px"></div>
    <div class="modal-foot"><button class="btn" data-close>Cancel</button><button class="btn primary" id="ooSave" disabled>Add to schedule</button></div>`);
  const q = $('#ooQ', m.box), results = $('#ooResults', m.box), eps = $('#ooEpisodes', m.box), save = $('#ooSave', m.box);
  let timer = null;
  q.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => searchItems(q.value, results, ['Movie', 'Series'], async (item, node) => {
    $$('.result', results).forEach((r) => r.classList.remove('selected')); node.classList.add('selected');
    if (item.Type === 'Series') {
      selected = null; save.disabled = true; eps.classList.remove('hidden'); eps.innerHTML = '<div class="loading"><span class="spinner"></span></div>';
      const list = await js(`series/${item.Id}/episodes`);
      eps.innerHTML = `<div class="field"><label>Episode</label><select id="ooEp"><option value="">Choose an episode…</option>${list.map((e) => `<option value="${e.Id}">${epCode(e.Season, e.Episode)} · ${h(e.Name)}${e.Watched ? ' ✓' : ''}</option>`).join('')}</select></div>`;
      $('#ooEp', eps).addEventListener('change', (ev) => { selected = ev.target.value || null; save.disabled = !selected; });
    } else { eps.classList.add('hidden'); selected = item.Id; save.disabled = false; }
  }), 250); });
  save.addEventListener('click', async () => {
    try {
      await js('one-offs', { method: 'POST', body: { Date: $('#ooDate', m.box).value, Start: $('#ooTime', m.box).value, ItemId: selected } });
      await loadState(); m.close(); toast('Added to the schedule'); route();
    } catch (e) { toast(e.message, true); }
  });
}

async function searchItems(term, container, types, onPick, opts = {}) {
  term = (term || '').trim();
  if (term.length < 2 && !opts.allowEmpty) { container.innerHTML = ''; return; }
  container.innerHTML = '<div class="loading"><span class="spinner"></span></div>';
  const seq = (container.__seq = (container.__seq || 0) + 1);
  try {
    const params = new URLSearchParams({ searchTerm: term, IncludeItemTypes: types.join(','), Recursive: 'true', Limit: '40', Fields: 'ProductionYear,Overview,Status', userId: auth.userId, EnableTotalRecordCount: 'false' });
    if (!term) { params.delete('searchTerm'); params.set('SortBy', 'DateCreated'); params.set('SortOrder', 'Descending'); }
    const r = await jf(`Items?${params}`);
    if (container.__seq !== seq) return; // a newer search has superseded this one
    container.innerHTML = '';
    if (!r.Items.length) { container.innerHTML = '<div class="muted small">No results.</div>'; return; }
    r.Items.forEach((it) => {
      const node = el(`<div class="result"><div class="poster">${it.ImageTags && it.ImageTags.Primary ? `<img src="${img(it.Id, 'Primary', 240)}" alt="" loading="lazy" onerror="this.remove()">` : `<div class="ph">${h(it.Name)}</div>`}</div><div class="cap">${h(it.Name)}<span class="muted">${h([it.ProductionYear, it.Type === 'BoxSet' ? 'Collection' : it.Type].filter(Boolean).join(' · '))}</span></div></div>`);
      node.addEventListener('click', () => onPick(it, node));
      container.appendChild(node);
    });
  } catch (e) { if (container.__seq === seq) container.innerHTML = `<div class="muted small">${h(e.message)}</div>`; }
}

// ---------------------------------------------------------------- now polling (keeps the hero fresh)
function startNowPolling() {
  clearInterval(state.nowTimer);
  state.nowTimer = setInterval(async () => {
    if (currentView() !== 'guide' || Player.active || document.hidden) return;
    try {
      const n = await js('now');
      const cur = state.guide && state.guide.OnNow ? state.guide.OnNow.Id : null;
      const next = n.OnNow ? n.OnNow.Id : null;
      if (cur !== next) route();
      else { const pill = $('#onair-slot'); if (pill) pill.innerHTML = n.OnNow ? '<span class="onair-pill"><span class="dot"></span> ON AIR</span>' : ''; }
    } catch { /* ignore */ }
  }, 30000);
}

// ============================================================ lineup
async function renderLineup(main) {
  const [lineup] = await Promise.all([js('lineup')]);
  state.lineup = lineup;
  const shows = lineup.filter((x) => x.Entry.Kind === 'Series');
  const movies = lineup.filter((x) => x.Entry.Kind !== 'Series');
  main.innerHTML = '';
  main.appendChild(el(`<h1>Lineup</h1>`));
  if (!state.st.Windows.length) main.appendChild(el(`<div class="warnbar">You haven't set any viewing times yet — <a href="#schedule">set them up</a> so the lineup has somewhere to go.</div>`));

  // Shows
  const sec = el(`<div class="section"><div class="section-head"><h2>Shows</h2><span class="muted small">${shows.length} in the lineup · order = priority</span>${canEdit() ? `<button class="btn primary right" id="addShow">${ICONS.plus} Add show</button>` : ''}</div><div class="lineup-grid" id="shows"></div></div>`);
  main.appendChild(sec);
  const grid = $('#shows', sec);
  if (!shows.length) grid.appendChild(el(`<div class="empty" style="grid-column:1/-1"><h3>No shows yet</h3><p>Add the series you want to follow. Each one gets one episode per airing, in order, skipping what you've already watched.</p></div>`));
  shows.forEach((s, i) => grid.appendChild(showCard(s, i, shows)));
  const add = $('#addShow', sec); if (add) add.addEventListener('click', () => addItemDialog('Series'));

  // Movie night
  main.appendChild(renderMovieNight(movies));
}

function showCard(s, i, all) {
  const e = s.Entry, it = s.Item || {};
  const days = (e.Days || []).map(dayName);
  const vd = viewingDays();
  const isRerun = e.Mode === 'ReRun';
  const pct = s.Total ? Math.round(s.Watched / s.Total * 100) : 0;
  const air = s.Airing;
  let airHtml = '';
  if (!isRerun) {
    if (s.Missing) airHtml = '<span class="badge warn">Missing from library</span>';
    else if (air && air.NextEpisode) airHtml = `<span class="up">Next on TV: ${epCode(air.NextEpisode.Season, air.NextEpisode.Episode)} ${h(relDate(air.NextEpisode.AirDate, serverNow()))}</span>${air.Network ? ' · ' + h(air.Network) : ''}${air.Source === 'sonarr' && !air.NextEpisode.HasFile ? ' · via Sonarr' : ''}`;
    else if (air && air.AiredNotAvailable && air.AiredNotAvailable.length) airHtml = `<span class="up">${air.AiredNotAvailable.length} aired episode${air.AiredNotAvailable.length > 1 ? 's' : ''} not downloaded yet</span> · Sonarr`;
    else if (it.Status === 'Ended') airHtml = 'Ended';
    else if (air && (air.Status || '').toLowerCase().includes('end')) airHtml = 'Ended';
    else if (air && air.Status) airHtml = `${h(air.Status)} · no upcoming date`;
    else if (it.Status) airHtml = h(it.Status);
  }
  const card = el(`<div class="show-card ${e.Paused ? 'paused' : ''}" data-id="${e.Id}">
    <div class="order">${i + 1}</div>
    ${posterHtml(it.HasPrimaryImage ? img(e.ItemId, 'Primary', 200) : null, '', it.Name || e.Name)}
    <div class="meta">
      <div class="name">${h(it.Name || e.Name)} ${isRerun ? '<span class="badge rerun">Re-runs</span>' : ''} ${e.Paused ? '<span class="badge">Paused</span>' : ''}</div>
      <div class="next">${isRerun ? `<span class="muted">${s.Total} episodes in the pool · random order</span>` : s.CaughtUp ? `<span class="muted">All caught up${s.Recorded ? ` · ${s.Recorded} recorded` : ''}</span>` : `Next: <b>${h(s.NextLabel || '')}</b>`}</div>
      ${isRerun ? '' : `<div class="progress" title="${s.Watched} of ${s.Total} watched"><i style="width:${pct}%"></i></div><div class="small muted" style="margin-top:3px">${s.Watched} of ${s.Total} watched${s.Remaining ? ` · ${s.Remaining} to go` : ''}${e.StartFrom ? ` · starting at ${h(e.StartFrom)}` : ''}</div>`}
      ${airHtml ? `<div class="air">${airHtml}</div>` : ''}
      <div class="cfg">
        <span class="small muted">Airs</span>
        <div class="daypick">${days.length ? days.map((d) => `<span class="chip on">${dayShort(d)}</span>`).join('') : '<span class="chip on">Any viewing day</span>'}</div>
        ${!isRerun && e.EpisodesPerAiring > 1 ? `<span class="badge">${e.EpisodesPerAiring} eps back-to-back</span>` : ''}
      </div>
    </div>
    ${canEdit() ? `<div class="tools">
      <button class="btn sm ghost" data-act="up" title="Higher priority" ${i === 0 ? 'disabled' : ''}>${ICONS.up}</button>
      <button class="btn sm ghost" data-act="down" title="Lower priority" ${i === all.length - 1 ? 'disabled' : ''}>${ICONS.down}</button>
      <button class="btn sm ghost" data-act="edit" title="Edit">${ICONS.edit}</button>
    </div>` : ''}
  </div>`);
  $$('[data-act]', card).forEach((b) => b.addEventListener('click', async () => {
    const act = b.dataset.act;
    if (act === 'edit') return editEntryDialog(s);
    const ids = all.map((x) => x.Entry.Id);
    const j = act === 'up' ? i - 1 : i + 1;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    const others = (state.lineup || []).filter((x) => x.Entry.Kind !== 'Series').map((x) => x.Entry.Id);
    await js('lineup/reorder', { method: 'POST', body: { Ids: ids.concat(others) } });
    route();
  }));
  return card;
}

function dayPicker(selected, opts = {}) {
  const vd = viewingDays();
  const sel = new Set((selected || []).map(dayName));
  const wrap = el('<div class="chips"></div>');
  const any = el(`<span class="chip ${sel.size === 0 ? 'on' : ''}">Any viewing day</span>`);
  any.addEventListener('click', () => { sel.clear(); refresh(); });
  wrap.appendChild(any);
  WEEK_ORDER.forEach((d) => {
    const enabled = vd.includes(d) || opts.allDays;
    const c = el(`<span class="chip ${sel.has(d) ? 'on' : ''} ${enabled ? '' : 'disabled'}" title="${enabled ? '' : 'Not a viewing day'}">${dayShort(d)}</span>`);
    if (enabled) c.addEventListener('click', () => { if (sel.has(d)) sel.delete(d); else sel.add(d); refresh(); });
    wrap.appendChild(c);
  });
  function refresh() { any.classList.toggle('on', sel.size === 0); $$('.chip', wrap).slice(1).forEach((c, i) => c.classList.toggle('on', sel.has(WEEK_ORDER[i]))); }
  wrap.value = () => Array.from(sel);
  return wrap;
}

function entryForm(entry, isSeries) {
  const f = el(`<div class="stack">
    ${isSeries ? `<div class="field"><label>How to play it</label><div class="seg" id="modeSeg"><button data-v="InOrder" class="${entry.Mode !== 'ReRun' ? 'on' : ''}">In order (first-run)</button><button data-v="ReRun" class="${entry.Mode === 'ReRun' ? 'on' : ''}">Re-runs (random)</button></div><div class="hint" id="modeHint"></div></div>` : ''}
    <div class="field"><label>Which days</label><div id="dayPick"></div><div class="hint">Pin it to specific viewing days, or let Jelly Schedule rotate it through any free evening.</div></div>
    ${isSeries ? `<div class="split">
      <div class="field" id="epaField"><label>Episodes per airing</label><select id="epa">${[1, 2, 3, 4].map((n) => `<option value="${n}" ${entry.EpisodesPerAiring === n ? 'selected' : ''}>${n}${n === 1 ? ' episode' : ' back-to-back'}</option>`).join('')}</select></div>
      <div class="field" id="sfField"><label>Start from</label><input type="text" id="startFrom" placeholder="e.g. S03E01 (optional)" value="${h(entry.StartFrom || '')}"><div class="hint">Earlier episodes are treated as already seen.</div></div>
    </div>` : ''}
  </div>`);
  const dp = dayPicker(entry.Days); $('#dayPick', f).replaceWith(dp);
  let mode = entry.Mode || 'InOrder';
  const hint = $('#modeHint', f);
  const setMode = (m) => { mode = m; $$('#modeSeg button', f).forEach((b) => b.classList.toggle('on', b.dataset.v === m)); if (hint) hint.textContent = m === 'ReRun' ? 'Random episodes fill leftover time; watched status is ignored. Perfect for sitcoms you know by heart.' : 'One new episode per airing, in order, skipping anything already watched. If you miss one it airs again next time.'; const epa = $('#epaField', f), sf = $('#sfField', f); if (epa) epa.classList.toggle('hidden', m === 'ReRun'); if (sf) sf.classList.toggle('hidden', m === 'ReRun'); };
  if (isSeries) { $$('#modeSeg button', f).forEach((b) => b.addEventListener('click', () => setMode(b.dataset.v))); setMode(mode); }
  f.value = () => ({ Mode: mode, Days: dp.value(), EpisodesPerAiring: isSeries ? +($('#epa', f).value) : 1, StartFrom: isSeries ? ($('#startFrom', f).value.trim() || null) : null });
  return f;
}

function addItemDialog(kind) {
  const isSeries = kind === 'Series';
  let picked = null;
  const m = modal(`<div class="modal-head"><h2>${isSeries ? 'Add a show' : 'Add movies'}</h2><button class="btn icon ghost" data-close>${ICONS.close}</button></div>
    <div id="step1">
      <div class="field"><input type="search" id="q" placeholder="${isSeries ? 'Search your series…' : 'Search movies, collections and playlists…'}" autofocus></div>
      <div class="muted small">Recently added</div>
      <div id="results" class="results"></div>
    </div>
    <div id="step2" class="hidden"></div>`, { wide: true });
  const results = $('#results', m.box), q = $('#q', m.box);
  const types = isSeries ? ['Series'] : ['Movie', 'BoxSet', 'Playlist'];
  const existing = new Set((state.lineup || []).map((x) => x.Entry.ItemId));
  const pick = (item, node) => {
    if (existing.has(item.Id)) { toast('Already in the lineup'); return; }
    picked = item;
    $('#step1', m.box).classList.add('hidden');
    const s2 = $('#step2', m.box); s2.classList.remove('hidden');
    s2.innerHTML = `<div class="detail" style="margin-bottom:14px">${posterHtml(item.ImageTags && item.ImageTags.Primary ? img(item.Id, 'Primary', 240) : null, '', item.Name)}<div class="meta"><h2>${h(item.Name)}</h2><div class="muted small">${h([item.ProductionYear, item.Type === 'BoxSet' ? 'Collection' : item.Type, item.Status].filter(Boolean).join(' · '))}</div><div class="ov">${h((item.Overview || '').slice(0, 260))}${(item.Overview || '').length > 260 ? '…' : ''}</div></div></div>`;
    const form = entryForm({ Mode: 'InOrder', Days: [], EpisodesPerAiring: 1 }, isSeries && item.Type === 'Series');
    s2.appendChild(form);
    if (!isSeries) s2.appendChild(el(`<p class="hint muted small">Movies air on your movie nights (set below). Collections and playlists are expanded, unwatched movies first.</p>`));
    s2.appendChild(el(`<div class="modal-foot"><button class="btn" id="back">${ICONS.left} Back</button><button class="btn primary" id="save">Add to lineup</button></div>`));
    $('#back', s2).addEventListener('click', () => { s2.classList.add('hidden'); $('#step1', m.box).classList.remove('hidden'); });
    $('#save', s2).addEventListener('click', async () => {
      try { const v = form.value(); await js('lineup', { method: 'POST', body: { ItemId: item.Id, ...v } }); m.close(); toast(`${item.Name} added`); route(); }
      catch (e) { toast(e.message, true); }
    });
  };
  searchItems('', results, types, pick, { allowEmpty: true });
  let timer = null;
  q.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => searchItems(q.value, results, types, pick, { allowEmpty: true }), 250); });
}

function editEntryDialog(s) {
  const e = s.Entry, isSeries = e.Kind === 'Series';
  const m = modal(`<div class="modal-head"><h2>${h(s.Item?.Name || e.Name)}</h2><button class="btn icon ghost" data-close>${ICONS.close}</button></div><div id="form"></div>
    <div class="modal-foot">
      <button class="btn danger" id="remove">${ICONS.trash} Remove</button>
      <button class="btn" id="pause">${e.Paused ? ICONS.play + ' Resume' : ICONS.pauseSmall + ' Pause'}</button>
      <span class="grow"></span>
      <button class="btn" data-close>Cancel</button><button class="btn primary" id="save">Save</button></div>`);
  const form = entryForm(e, isSeries); $('#form', m.box).replaceWith(form);
  $('#save', m.box).addEventListener('click', async () => {
    try { const v = form.value(); await js(`lineup/${e.Id}`, { method: 'PUT', body: { ...v, ClearStartFrom: isSeries && !v.StartFrom } }); m.close(); toast('Saved'); route(); }
    catch (err) { toast(err.message, true); }
  });
  $('#pause', m.box).addEventListener('click', async () => { await js(`lineup/${e.Id}`, { method: 'PUT', body: { Paused: !e.Paused } }); m.close(); route(); });
  $('#remove', m.box).addEventListener('click', async () => {
    if (!await confirmDialog('Remove from lineup?', `${s.Item?.Name || e.Name} will no longer be scheduled. Nothing in your library changes.`, 'Remove', true)) return;
    await js(`lineup/${e.Id}`, { method: 'DELETE' }); m.close(); toast('Removed'); route();
  });
}

function renderMovieNight(movies) {
  const mn = state.st.MovieNight || { Days: [] };
  const sec = el(`<div class="section"><div class="section-head"><h2>Movie night</h2><span class="muted small">${movies.length} source${movies.length === 1 ? '' : 's'}</span>${canEdit() ? `<button class="btn primary right" id="addMovie">${ICONS.plus} Add movies</button>` : ''}</div>
    <div class="card" style="margin-bottom:12px">
      <div class="row wrap" style="gap:18px;align-items:flex-start">
        <div class="field" style="margin:0"><label>Movie nights</label><div id="mnDays"></div></div>
        <div class="field" style="margin:0"><label>Position</label><div class="seg" id="mnPos"><button data-v="Start" class="${mn.Position !== 'End' ? 'on' : ''}">Start of the evening</button><button data-v="End" class="${mn.Position === 'End' ? 'on' : ''}">End of the evening</button></div></div>
        <div class="field" style="margin:0"><label>Order</label><div class="seg" id="mnOrder"><button data-v="AsAdded" class="${mn.Order !== 'Shuffle' ? 'on' : ''}">As added</button><button data-v="Shuffle" class="${mn.Order === 'Shuffle' ? 'on' : ''}">Shuffle</button></div></div>
        <div class="field" style="margin:0"><label>Fixed start (optional)</label><input type="time" id="mnTime" value="${h(mn.StartTime || '')}" step="900" style="width:120px"></div>
        ${canEdit() ? '<button class="btn" id="mnSave" style="align-self:flex-end">Save</button>' : ''}
      </div>
      <div class="hint muted small" style="margin-top:8px">One movie per movie night, longest first? No — in the order you add them (or shuffled). Watched movies are skipped automatically. Add a collection or playlist to keep the queue topped up.</div>
    </div>
    <div class="lineup-grid" id="movies"></div></div>`);
  const dp = dayPicker(mn.Days, { allDays: false }); $('#mnDays', sec).replaceWith(dp);
  let pos = mn.Position || 'Start', order = mn.Order || 'AsAdded';
  $$('#mnPos button', sec).forEach((b) => b.addEventListener('click', () => { pos = b.dataset.v; $$('#mnPos button', sec).forEach((x) => x.classList.toggle('on', x === b)); }));
  $$('#mnOrder button', sec).forEach((b) => b.addEventListener('click', () => { order = b.dataset.v; $$('#mnOrder button', sec).forEach((x) => x.classList.toggle('on', x === b)); }));
  const save = $('#mnSave', sec); if (save) save.addEventListener('click', async () => {
    try { await js('movie-night', { method: 'PUT', body: { Days: dp.value(), Position: pos, Order: order, StartTime: $('#mnTime', sec).value || null } }); await loadState(); toast('Movie night saved'); }
    catch (e) { toast(e.message, true); }
  });
  const grid = $('#movies', sec);
  if (!movies.length) grid.appendChild(el(`<div class="empty" style="grid-column:1/-1"><h3>No movies queued</h3><p>Add individual movies, a collection, or a playlist. Remember Sunday night blockbusters?</p></div>`));
  movies.forEach((s) => {
    const e = s.Entry, it = s.Item || {};
    const isPool = e.Kind !== 'Movie';
    const card = el(`<div class="show-card">
      ${posterHtml(it.HasPrimaryImage ? img(e.ItemId, 'Primary', 200) : null, '', it.Name || e.Name)}
      <div class="meta">
        <div class="name">${h(it.Name || e.Name)} <span class="badge movie">${isPool ? (e.Kind === 'Collection' ? 'Collection' : 'Playlist') : 'Movie'}</span></div>
        <div class="next">${s.Missing ? '<span class="badge warn">Missing from library</span>' : isPool ? (s.Remaining ? `Next: <b>${h(s.NextLabel || '')}</b>` : '<span class="muted">All watched</span>') : (s.Watched ? '<span class="muted">Watched</span>' : `<span class="muted">${it.Year || ''}${it.RuntimeMinutes ? ' · ' + fmtDur(it.RuntimeMinutes) : ''}</span>`)}</div>
        ${isPool ? `<div class="small muted" style="margin-top:4px">${s.Remaining} of ${s.Total} still to watch</div>` : ''}
      </div>
      ${canEdit() ? `<div class="tools"><button class="btn sm ghost" data-act="remove" title="Remove">${ICONS.trash}</button></div>` : ''}
    </div>`);
    const rm = $('[data-act=remove]', card); if (rm) rm.addEventListener('click', async () => { if (!await confirmDialog('Remove from movie night?', it.Name || e.Name, 'Remove', true)) return; await js(`lineup/${e.Id}`, { method: 'DELETE' }); route(); });
    grid.appendChild(card);
  });
  const add = $('#addMovie', sec); if (add) add.addEventListener('click', () => addItemDialog('Movie'));

  if (state.st.Integrations && state.st.Integrations.Radarr) {
    const cs = el(`<div style="margin-top:12px"><div class="muted small" style="margin-bottom:6px">Coming soon on Radarr</div><div class="comingsoon" id="radarr"><div class="loading"><span class="spinner"></span></div></div></div>`);
    sec.appendChild(cs);
    js('radarr/upcoming').then((list) => { const box = $('#radarr', cs); box.innerHTML = ''; if (!list.length) box.innerHTML = '<span class="muted small">Nothing waiting.</span>'; list.slice(0, 12).forEach((mv) => box.appendChild(el(`<div class="cs"><div class="d">${mv.DigitalRelease ? 'Digital ' + h(relDate(mv.DigitalRelease)) : mv.InCinemas ? 'Cinemas ' + h(relDate(mv.InCinemas)) : 'TBA'}</div><div class="t">${h(mv.Title)}</div><div class="muted">${mv.Year || ''}${mv.IsAvailable ? ' · available' : ''}</div></div>`))); }).catch(() => { $('#radarr', cs).innerHTML = '<span class="muted small">Radarr unreachable.</span>'; });
  }
  return sec;
}

// ============================================================ schedule (viewing windows, away dates)
async function renderSchedule(main) {
  await loadState();
  const windows = (state.st.Windows || []).map((w) => ({ ...w, Days: (w.Days || []).map(dayName) }));
  main.innerHTML = '';
  main.appendChild(el('<h1>Schedule</h1>'));
  const sec = el(`<div class="section"><div class="section-head"><h2>Viewing times</h2><span class="muted small">When is the channel on air?</span>${canEdit() ? `<span class="right row"><button class="btn" id="addWin">${ICONS.plus} Add times</button><button class="btn primary" id="saveWin">Save</button></span>` : ''}</div>
    <p class="muted small">Pick the evenings (and hours) you actually want to watch. Everything is cut into ${settings().SlotMinutes || 30}-minute slots, like a real TV listing — and when the window ends, the channel goes off air.</p>
    <div class="windows" id="windows"></div>
    <div class="card" style="margin-top:14px"><h3>Your week at a glance</h3><div id="preview"></div></div></div>`);
  main.appendChild(sec);
  const list = $('#windows', sec);
  const draw = () => {
    list.innerHTML = '';
    if (!windows.length) list.appendChild(el(`<div class="empty"><h3>No viewing times yet</h3><p>Add one — for example Tuesday and Thursday from 20:00 to 22:00, and Sunday from 19:30 to 22:30.</p></div>`));
    windows.forEach((w, i) => {
      const row = el(`<div class="window">
        <div id="dp"></div>
        <div class="row"><input type="time" value="${h(w.Start)}" step="900" data-k="Start"><span class="muted">to</span><input type="time" value="${h(w.End)}" step="900" data-k="End"></div>
        <input type="text" class="lbl-in" placeholder="Label (optional)" value="${h(w.Label || '')}" data-k="Label">
        ${canEdit() ? `<button class="btn icon ghost" title="Remove">${ICONS.trash}</button>` : '<span></span>'}
      </div>`);
      const dp = dayPicker(w.Days, { allDays: true }); $$('.chip', dp)[0].remove(); $('#dp', row).replaceWith(dp);
      // day picker: selection updates
      $$('.chip', dp).forEach((c, idx) => c.addEventListener('click', () => { w.Days = dp.value(); preview(); }));
      $$('input', row).forEach((inp) => inp.addEventListener('change', () => { w[inp.dataset.k] = inp.value; preview(); }));
      const del = $('button', row); if (del) del.addEventListener('click', () => { windows.splice(i, 1); draw(); });
      list.appendChild(row);
    });
    preview();
  };
  const preview = () => {
    const box = $('#preview', sec); box.innerHTML = '';
    const start = 17 * 60, end = 25 * 60, step = 30;
    const grid = el('<div class="weekpreview"></div>');
    grid.appendChild(el('<div></div>'));
    WEEK_ORDER.forEach((d) => grid.appendChild(el(`<div class="h">${dayShort(d)}</div>`)));
    const mnDays = new Set((state.st.MovieNight?.Days || []).map(dayName));
    for (let m = start; m < end; m += step) {
      grid.appendChild(el(`<div class="t">${m % 60 === 0 ? minToHm(m) : ''}</div>`));
      WEEK_ORDER.forEach((d) => {
        const on = windows.some((w) => (w.Days || []).includes(d) && inWindow(w, m));
        grid.appendChild(el(`<div class="c ${on ? (mnDays.has(d) ? 'movie' : 'on') : ''}"></div>`));
      });
    }
    box.appendChild(grid);
    const total = WEEK_ORDER.reduce((acc, d) => acc + windows.filter((w) => (w.Days || []).includes(d)).reduce((a2, w) => a2 + winLen(w), 0), 0);
    box.appendChild(el(`<div class="muted small" style="margin-top:8px">${fmtDur(total)} of viewing per week${mnDays.size ? ' · blue = movie night' : ''}</div>`));
  };
  const inWindow = (w, m) => { const s = hmToMin(w.Start); let e = hmToMin(w.End); if (e <= s) e += 1440; return m >= s && m < e; };
  const winLen = (w) => { const s = hmToMin(w.Start); let e = hmToMin(w.End); if (e <= s) e += 1440; return e - s; };
  draw();
  const add = $('#addWin', sec); if (add) add.addEventListener('click', () => { windows.push({ Days: windows.length ? [] : ['Tuesday', 'Thursday'], Start: '20:00', End: '22:00', Label: '' }); draw(); });
  const save = $('#saveWin', sec); if (save) save.addEventListener('click', async () => {
    if (windows.some((w) => !(w.Days || []).length)) { toast('Each viewing time needs at least one day', true); return; }
    try { await js('windows', { method: 'PUT', body: windows }); await loadState(); toast('Viewing times saved'); }
    catch (e) { toast(e.message, true); }
  });

  // Away dates
  const blackouts = (state.st.Blackouts || []).map((b) => ({ ...b }));
  const bsec = el(`<div class="section"><div class="section-head"><h2>Away dates</h2><span class="muted small">Holidays, travel — the channel goes dark and nothing counts as missed.</span>${canEdit() ? `<span class="right row"><button class="btn" id="addBo">${ICONS.plus} Add dates</button><button class="btn primary" id="saveBo">Save</button></span>` : ''}</div><div class="list" id="bo"></div></div>`);
  main.appendChild(bsec);
  const blist = $('#bo', bsec);
  const drawB = () => {
    blist.innerHTML = '';
    if (!blackouts.length) blist.appendChild(el('<div class="muted small">None planned.</div>'));
    blackouts.forEach((b, i) => {
      const row = el(`<div class="item-row"><input type="text" placeholder="Label" value="${h(b.Label || 'Away')}" data-k="Label" style="max-width:200px"><input type="date" value="${h(b.From)}" data-k="From" style="max-width:170px"><span class="muted">to</span><input type="date" value="${h(b.To)}" data-k="To" style="max-width:170px">${canEdit() ? `<button class="btn icon ghost right">${ICONS.trash}</button>` : ''}</div>`);
      $$('input', row).forEach((inp) => inp.addEventListener('change', () => { b[inp.dataset.k] = inp.value; }));
      const del = $('button', row); if (del) del.addEventListener('click', () => { blackouts.splice(i, 1); drawB(); });
      blist.appendChild(row);
    });
  };
  drawB();
  const addB = $('#addBo', bsec); if (addB) addB.addEventListener('click', () => { const t = wall(serverNow()).date; blackouts.push({ Label: 'Away', From: t, To: addDays(t, 6) }); drawB(); });
  const saveB = $('#saveBo', bsec); if (saveB) saveB.addEventListener('click', async () => { try { await js('blackouts', { method: 'PUT', body: blackouts }); await loadState(); toast('Away dates saved'); } catch (e) { toast(e.message, true); } });

  // One-offs
  const oneOffs = state.st.OneOffs || [];
  const osec = el(`<div class="section"><div class="section-head"><h2>One-offs</h2><span class="muted small">Specific programmes on specific evenings.</span>${canEdit() ? `<button class="btn right" id="addOo">${ICONS.plus} Add one-off</button>` : ''}</div><div class="list" id="oo"></div></div>`);
  main.appendChild(osec);
  const olist = $('#oo', osec);
  if (!oneOffs.length) olist.innerHTML = '<div class="muted small">None. You can also click any empty slot in the guide.</div>';
  oneOffs.forEach((o) => {
    const row = el(`<div class="item-row"><div class="grow"><div class="title">${h(o.Name)}</div><div class="sub">${fmtDateLong(o.Date)} at ${h(o.Start)}</div></div>${canEdit() ? `<button class="btn icon ghost">${ICONS.trash}</button>` : ''}</div>`);
    const del = $('button', row); if (del) del.addEventListener('click', async () => { await js(`one-offs/${o.Id}`, { method: 'DELETE' }); await loadState(); route(); });
    olist.appendChild(row);
  });
  const addO = $('#addOo', osec); if (addO) addO.addEventListener('click', () => oneOffDialog());
}

// ============================================================ recordings
async function renderRecordings(main) {
  const recs = await js('recordings');
  state.recordings = recs;
  main.innerHTML = '';
  const pending = recs.filter((r) => !r.IsWatched), done = recs.filter((r) => r.IsWatched);
  main.appendChild(el(`<h1>Recordings</h1><p class="muted small" style="margin-top:-8px">Programmes you set aside to watch later. The schedule has already moved on without them — watch these whenever you like.</p>`));
  const sec = el(`<div class="section"><div class="section-head"><h2>To watch</h2><span class="muted small">${pending.length}</span></div><div class="list" id="pending"></div></div>`);
  main.appendChild(sec);
  const list = $('#pending', sec);
  if (!pending.length) list.appendChild(el('<div class="empty"><h3>Nothing recorded</h3><p>When you can\'t make a slot, hit <b>Record</b> on the programme. It lands here and the show keeps going.</p></div>'));
  pending.forEach((r) => list.appendChild(recCard(r)));
  if (done.length) {
    const dsec = el(`<div class="section"><div class="section-head"><h2>Watched</h2><span class="muted small">${done.length}</span>${canEdit() ? '<button class="btn sm right" id="clearDone">Clear</button>' : ''}</div><div class="list" id="done"></div></div>`);
    main.appendChild(dsec);
    done.forEach((r) => $('#done', dsec).appendChild(recCard(r)));
    const c = $('#clearDone', dsec); if (c) c.addEventListener('click', async () => { await js('recordings/watched', { method: 'DELETE' }); route(); });
  }
}

function recCard(r) {
  const it = r.Item || {};
  const isEp = r.Season != null || r.Episode != null;
  const posterId = isEp && r.SeriesId ? r.SeriesId : r.Recording.ItemId;
  const pct = it.RuntimeMinutes && r.PositionTicks ? Math.min(100, r.PositionTicks / 1e7 / 60 / it.RuntimeMinutes * 100) : 0;
  const card = el(`<div class="rec-card ${r.IsWatched ? 'watched' : ''}">
    ${posterHtml(r.Missing ? null : img(posterId, 'Primary', 160), '', r.Recording.Title)}
    <div class="meta">
      <div class="t">${h(r.Recording.Title)}</div>
      <div class="s">${h(r.Recording.Subtitle || '')}${it.RuntimeMinutes ? ' · ' + fmtDur(it.RuntimeMinutes) : ''}</div>
      <div class="s">Recorded ${h(relDate(r.Recording.RecordedAt))}${r.Missing ? ' · <span class="badge warn">missing from library</span>' : ''}${r.IsWatched ? ' · <span class="badge ok">watched</span>' : ''}</div>
      ${pct ? `<div class="progress" style="margin-top:6px;max-width:240px"><i style="width:${pct}%"></i></div>` : ''}
    </div>
    <div class="row">
      ${r.Missing ? '' : `<button class="btn primary" data-act="play">${ICONS.play} ${r.PositionTicks ? 'Resume' : 'Watch'}</button>`}
      ${r.Missing ? '' : `<button class="btn icon" data-act="cast" title="Play on…">${ICONS.cast}</button>`}
      ${canEdit() ? `<button class="btn icon ghost" data-act="del" title="${r.IsWatched ? 'Remove' : 'Cancel recording (put it back on the schedule)'}">${r.IsWatched ? ICONS.trash : ICONS.undo}</button>` : ''}
    </div></div>`);
  const asAiring = { ItemId: r.Recording.ItemId, Kind: isEp ? 'Episode' : 'Movie', Title: isEp ? (r.Recording.Subtitle || '').split(' · ').slice(1).join(' · ') : r.Recording.Title, SeriesName: isEp ? r.Recording.Title : null, SeriesId: r.SeriesId, Season: r.Season, Episode: r.Episode, Overview: it.Overview, RuntimeMinutes: it.RuntimeMinutes || 0, PositionTicks: r.PositionTicks, HasPrimaryImage: it.HasPrimaryImage, SeriesHasPrimaryImage: !!r.SeriesId, HasBackdrop: it.HasBackdrop, IsOnNow: false };
  $$('[data-act]', card).forEach((b) => b.addEventListener('click', async () => {
    const act = b.dataset.act;
    if (act === 'play') Player.playAiring(asAiring, { live: false, resume: true, recording: true });
    else if (act === 'cast') castDialog(asAiring);
    else if (act === 'del') { await js(`recordings/${r.Recording.Id}`, { method: 'DELETE' }); toast(r.IsWatched ? 'Removed' : 'Back on the schedule'); route(); }
  }));
  return card;
}

// ============================================================ settings
async function renderSettings(main) {
  await loadState();
  const s = settings(), st = state.st;
  const isAdmin = st.Me.IsAdmin;
  const calUrl = `${location.origin}${base}/JellySchedule/calendar.ics?key=${encodeURIComponent(s.CalendarKey || '')}`;
  main.innerHTML = '';
  main.appendChild(el('<h1>Settings</h1>'));
  const grid = el('<div class="settings-grid"></div>');
  main.appendChild(grid);

  grid.appendChild(el(`<div class="card stack">
    <h2>Watching</h2>
    <div class="field"><label>Tuning in</label>
      <div class="seg" id="liveMode"><button data-v="Relaxed" class="${s.LiveMode !== 'Strict' ? 'on' : ''}">Relaxed</button><button data-v="Strict" class="${s.LiveMode === 'Strict' ? 'on' : ''}">Strict</button></div>
      <div class="hint"><b>Relaxed:</b> during the slot, the programme starts from the beginning (or where you left off). <b>Strict:</b> join in progress, exactly like broadcast TV.</div></div>
    <div class="field"><label class="toggle"><input type="checkbox" id="autoplay" ${s.AutoplayOnOpen ? 'checked' : ''}><span class="sw"></span> Offer to tune in automatically when something is on</label></div>
    <div class="field"><label>Slot length</label><div class="seg" id="slot"><button data-v="30" class="${s.SlotMinutes !== 60 ? 'on' : ''}">30 minutes</button><button data-v="60" class="${s.SlotMinutes === 60 ? 'on' : ''}">60 minutes</button></div><div class="hint">Programmes are rounded up to whole slots, like a printed TV listing.</div></div>
    <div class="field"><label>Leftover time</label><select id="fill"><option value="Reruns" ${s.FillMode === 'Reruns' ? 'selected' : ''}>Fill with re-runs</option><option value="Nothing" ${s.FillMode === 'Nothing' ? 'selected' : ''}>Leave it off air</option><option value="MoreEpisodes" ${s.FillMode === 'MoreEpisodes' ? 'selected' : ''}>More episodes of first-run shows, then re-runs</option></select></div>
    <div class="field"><label>Re-run picker</label><select id="rerun"><option value="Random" ${s.RerunPicker !== 'RoundRobin' ? 'selected' : ''}>Random show</option><option value="RoundRobin" ${s.RerunPicker === 'RoundRobin' ? 'selected' : ''}>Rotate through shows</option></select></div>
    <div class="field"><label class="toggle"><input type="checkbox" id="specials" ${s.IncludeSpecials ? 'checked' : ''}><span class="sw"></span> Include specials (season 0)</label></div>
    <div class="field"><label>Time zone</label><input type="text" id="tz" value="${h(s.TimeZoneId || '')}" placeholder="${h(st.ServerTimeZone)} (server default)"><div class="hint">IANA name such as Europe/Amsterdam. Leave empty to use the server's zone (${h(st.ServerTimeZone)}).</div></div>
    ${isAdmin ? `<div class="field"><label>Household user</label><select id="household">${st.Users.map((u) => `<option value="${u.Id}" ${st.HouseholdUser && st.HouseholdUser.Id === u.Id ? 'selected' : ''}>${h(u.Name)}${u.IsAdmin ? ' (admin)' : ''}</option>`).join('')}</select><div class="hint">The schedule follows this user's watched history. Sign in as this user on the TV, or let Jelly Schedule mirror playback to it.</div></div>
    <div class="field"><label class="toggle"><input type="checkbox" id="adminOnly" ${s.AdminOnlyEditing ? 'checked' : ''}><span class="sw"></span> Only administrators can change the schedule</label></div>` : `<div class="kv"><b>Household user</b><span>${h(st.HouseholdUser ? st.HouseholdUser.Name : '—')}</span></div>`}
    ${canEdit() ? '<button class="btn primary" id="saveSettings">Save</button>' : ''}
  </div>`));

  grid.appendChild(el(`<div class="card stack">
    <h2>Calendar feed</h2>
    <p class="muted small">Subscribe from your phone's calendar so the whole household sees what's on and when. Anyone with this link can read your schedule.</p>
    <input type="text" readonly value="${h(calUrl)}" onclick="this.select()">
    <div class="row"><a class="btn" href="${h(calUrl)}">Download .ics</a><button class="btn" id="copyCal">Copy link</button></div>
    <h2 style="margin-top:8px">Integrations</h2>
    <div class="kv">
      <b>Sonarr</b><span>${st.Integrations.Sonarr ? '<span class="badge ok">connected</span>' : '<span class="badge">not configured</span>'}</span>
      <b>Radarr</b><span>${st.Integrations.Radarr ? '<span class="badge ok">connected</span>' : '<span class="badge">not configured</span>'}</span>
      <b>TVmaze</b><span>${st.Integrations.TvMaze ? '<span class="badge ok">enabled</span> <span class="muted small">no key needed</span>' : '<span class="badge">off</span>'}</span>
    </div>
    <p class="muted small">Sonarr and Radarr keys are set by an administrator in <a href="${base}/web/index.html#/dashboard/plugins" target="_blank">Jellyfin Dashboard → Plugins → Jelly Schedule</a>. Without Sonarr, TVmaze provides air dates for free.</p>
    <div class="row"><button class="btn" id="refreshAir">${ICONS.refresh} Refresh air dates</button>${canEdit() ? `<button class="btn ghost" id="regen" title="Rebuild the rest of today">Rebuild today</button>` : ''}</div>
    <h2 style="margin-top:8px">About</h2>
    <p class="muted small">Jelly Schedule ${h(st.Version)} · signed in as ${h(st.Me.Name)}${st.Me.IsAdmin ? ' (admin)' : ''}.<br>Keyboard in the player: <kbd>space</kbd> play/pause · <kbd>←</kbd>/<kbd>→</kbd> ±10s · <kbd>f</kbd> fullscreen · <kbd>c</kbd> subtitles · <kbd>esc</kbd> close.</p>
  </div>`));

  let live = s.LiveMode || 'Relaxed', slot = s.SlotMinutes || 30;
  $$('#liveMode button', grid).forEach((b) => b.addEventListener('click', () => { live = b.dataset.v; $$('#liveMode button', grid).forEach((x) => x.classList.toggle('on', x === b)); }));
  $$('#slot button', grid).forEach((b) => b.addEventListener('click', () => { slot = +b.dataset.v; $$('#slot button', grid).forEach((x) => x.classList.toggle('on', x === b)); }));
  const save = $('#saveSettings', grid); if (save) save.addEventListener('click', async () => {
    try {
      await js('settings', { method: 'PUT', body: { ...s, LiveMode: live, SlotMinutes: slot, AutoplayOnOpen: $('#autoplay', grid).checked, FillMode: $('#fill', grid).value, RerunPicker: $('#rerun', grid).value, IncludeSpecials: $('#specials', grid).checked, TimeZoneId: $('#tz', grid).value.trim(), HouseholdUserId: isAdmin ? $('#household', grid).value : s.HouseholdUserId, AdminOnlyEditing: isAdmin ? $('#adminOnly', grid).checked : s.AdminOnlyEditing } });
      await loadState(); toast('Settings saved');
    } catch (e) { toast(e.message, true); }
  });
  $('#copyCal', grid).addEventListener('click', async () => { try { await navigator.clipboard.writeText(calUrl); toast('Link copied'); } catch { toast('Copy failed — select the text instead', true); } });
  $('#refreshAir', grid).addEventListener('click', async () => { const b = $('#refreshAir', grid); b.disabled = true; try { await js('integrations/refresh', { method: 'POST' }); toast('Air dates refreshed'); } catch (e) { toast(e.message, true); } b.disabled = false; });
  const rg = $('#regen', grid); if (rg) rg.addEventListener('click', async () => { await js('regenerate', { method: 'POST' }); toast('Today rebuilt'); });
}

// ============================================================ player
const Player = {
  active: false, root: null, video: null, hls: null, airing: null, opts: null, session: null, progressTimer: null, idleTimer: null, mirrorTimer: null, menuOpen: null, ended: false,

  deviceProfile() {
    const v = document.createElement('video');
    const can = (t) => !!v.canPlayType(t).replace(/no/, '');
    const vc = ['h264'];
    if (can('video/mp4; codecs="hvc1.1.6.L93.B0"') || can('video/mp4; codecs="hev1.1.6.L93.B0"')) vc.push('hevc');
    if (can('video/mp4; codecs="av01.0.08M.08"')) vc.push('av1');
    if (can('video/webm; codecs="vp9"')) vc.push('vp9');
    const ac = ['aac', 'mp3', 'opus', 'flac', 'vorbis'];
    if (can('audio/mp4; codecs="ac-3"')) ac.push('ac3');
    if (can('audio/mp4; codecs="ec-3"')) ac.push('eac3');
    return {
      Name: 'Jelly Schedule Web', MaxStreamingBitrate: 120000000, MaxStaticBitrate: 120000000, MusicStreamingTranscodingBitrate: 384000,
      DirectPlayProfiles: [
        { Container: 'mp4,m4v', Type: 'Video', VideoCodec: vc.join(','), AudioCodec: ac.join(',') },
        { Container: 'mkv', Type: 'Video', VideoCodec: vc.join(','), AudioCodec: ac.join(',') },
        { Container: 'webm', Type: 'Video', VideoCodec: 'vp8,vp9,av1', AudioCodec: 'vorbis,opus' }
      ],
      TranscodingProfiles: [{ Container: 'ts', Type: 'Video', VideoCodec: 'h264', AudioCodec: 'aac,mp3', Protocol: 'hls', Context: 'Streaming', MaxAudioChannels: '2', MinSegments: '1', BreakOnNonKeyFrames: true }],
      ContainerProfiles: [],
      CodecProfiles: [{ Type: 'Video', Codec: 'h264', Conditions: [{ Condition: 'NotEquals', Property: 'IsAnamorphic', Value: 'true', IsRequired: false }, { Condition: 'LessThanEqual', Property: 'VideoLevel', Value: '52', IsRequired: false }] }],
      SubtitleProfiles: [{ Format: 'vtt', Method: 'External' }, { Format: 'subrip', Method: 'External' }, { Format: 'srt', Method: 'External' }, { Format: 'ass', Method: 'External' }, { Format: 'ssa', Method: 'External' }],
      ResponseProfiles: []
    };
  },

  ensureDom() {
    if (this.root) return;
    this.root = el(`<div class="player">
      <video playsinline crossorigin="anonymous"></video>
      <div class="overlay">
        <div class="top"><button class="btn icon" id="pClose" title="Close (esc)">${ICONS.close}</button><div><div class="t" id="pTitle"></div><div class="s" id="pSub"></div></div></div>
        <div class="bug" id="pBug"></div>
        <div class="center-msg" id="pCenter"></div>
        <div class="bottom">
          <div class="seek" id="pSeek"><b id="pBuf"></b><i id="pFill"></i></div>
          <div class="ctl">
            <button class="btn icon" id="pPlay">${ICONS.play}</button>
            <button class="btn icon" id="pBack" title="-10s">${ICONS.back10}</button>
            <button class="btn icon" id="pFwd" title="+10s">${ICONS.fwd10}</button>
            <span class="time" id="pTime">0:00 / 0:00</span>
            <span class="grow"></span>
            <span class="muted small" id="pNext"></span>
            <input type="range" id="pVol" min="0" max="1" step="0.05" value="1" title="Volume">
            <button class="btn icon" id="pCc" title="Subtitles (c)">${ICONS.cc}</button>
            <button class="btn icon" id="pAudio" title="Audio">${ICONS.audio}</button>
            <button class="btn icon" id="pFull" title="Fullscreen (f)">${ICONS.full}</button>
          </div>
        </div>
      </div>
      <div id="pInter" class="hidden"></div>
      <div id="pErr" class="hidden"></div>
    </div>`);
    document.body.appendChild(this.root);
    const v = this.video = $('video', this.root);
    const q = (s) => $(s, this.root);
    q('#pClose').addEventListener('click', () => this.close());
    q('#pPlay').addEventListener('click', () => this.togglePlay());
    q('#pBack').addEventListener('click', () => this.seekBy(-10));
    q('#pFwd').addEventListener('click', () => this.seekBy(10));
    q('#pFull').addEventListener('click', () => this.toggleFullscreen());
    q('#pVol').addEventListener('input', (e) => { v.volume = +e.target.value; v.muted = v.volume === 0; store.set('jellyschedule.volume', v.volume); });
    q('#pCc').addEventListener('click', () => this.toggleMenu('sub'));
    q('#pAudio').addEventListener('click', () => this.toggleMenu('audio'));
    q('#pSeek').addEventListener('click', (e) => { const r = e.currentTarget.getBoundingClientRect(); const pct = (e.clientX - r.left) / r.width; if (v.duration) { v.currentTime = pct * v.duration; this.report('progress'); } });
    v.addEventListener('click', () => this.togglePlay());
    v.addEventListener('dblclick', () => this.toggleFullscreen());
    v.addEventListener('timeupdate', () => this.updateTime());
    v.addEventListener('progress', () => this.updateTime());
    v.addEventListener('play', () => { q('#pPlay').innerHTML = ICONS.pause; this.report('progress'); });
    v.addEventListener('pause', () => { q('#pPlay').innerHTML = ICONS.play; this.report('progress'); });
    v.addEventListener('ended', () => this.onEnded());
    v.addEventListener('error', () => this.onError(v.error ? `Playback error (${v.error.code})` : 'Playback error'));
    v.addEventListener('waiting', () => { if (!this.ended) q('#pCenter').innerHTML = '<span class="spinner"></span>'; });
    v.addEventListener('playing', () => { q('#pCenter').innerHTML = ''; });
    this.root.addEventListener('mousemove', () => this.wake());
    this.root.addEventListener('touchstart', () => this.wake(), { passive: true });
    document.addEventListener('keydown', (e) => this.onKey(e));
    const vol = store.get('jellyschedule.volume'); if (typeof vol === 'number') { v.volume = vol; q('#pVol').value = vol; }
  },

  async playAiring(airing, opts = {}) {
    this.ensureDom();
    this.airing = airing; this.opts = opts; this.ended = false;
    this.active = true; this.root.classList.remove('hidden'); document.body.style.overflow = 'hidden';
    $('#pInter', this.root).classList.add('hidden'); $('#pErr', this.root).classList.add('hidden');
    $('#pTitle', this.root).textContent = airingTitle(airing);
    $('#pSub', this.root).textContent = airingSub(airing);
    $('#pBug', this.root).innerHTML = opts.live ? '<span class="onair-pill"><span class="dot"></span> LIVE</span>' : opts.recording ? '<span class="badge rec">Recording</span>' : '';
    $('#pNext', this.root).textContent = '';
    let startTicks = 0;
    if (opts.startTicks != null) startTicks = opts.startTicks;
    else if (opts.live && settings().LiveMode === 'Strict') { const off = ms(serverNow()) - ms(airing.Start); startTicks = off > 0 && off < (airing.RuntimeMinutes || 0) * 60000 ? off * 1e4 : 0; }
    else if (airing.PositionTicks > 0 && (opts.resume || opts.live)) startTicks = airing.PositionTicks;
    await this.start({ startTicks, audioIndex: opts.audioIndex, subIndex: opts.subIndex, forceTranscode: !!opts.forceTranscode });
    this.wake();
  },

  async start({ startTicks = 0, audioIndex, subIndex, forceTranscode = false }) {
    const v = this.video; const a = this.airing;
    this.teardownMedia();
    $('#pCenter', this.root).innerHTML = '<span class="spinner"></span>';
    try {
      const body = { UserId: auth.userId, DeviceProfile: this.deviceProfile(), MaxStreamingBitrate: 120000000, StartTimeTicks: startTicks, EnableDirectPlay: !forceTranscode, EnableDirectStream: !forceTranscode, EnableTranscoding: true, AllowVideoStreamCopy: true, AllowAudioStreamCopy: true, AutoOpenLiveStream: true };
      if (audioIndex != null) body.AudioStreamIndex = audioIndex;
      if (subIndex != null) body.SubtitleStreamIndex = subIndex;
      const info = await jf(`Items/${a.ItemId}/PlaybackInfo?userId=${auth.userId}`, { method: 'POST', body });
      if (info.ErrorCode) throw new Error(info.ErrorCode);
      const src = (info.MediaSources || [])[0];
      if (!src) throw new Error('No playable media source');
      let url, method;
      if (src.SupportsDirectPlay || src.SupportsDirectStream) {
        method = src.SupportsDirectPlay ? 'DirectPlay' : 'DirectStream';
        url = `${base}/Videos/${a.ItemId}/stream.${src.Container || 'mp4'}?static=true&mediaSourceId=${encodeURIComponent(src.Id)}&deviceId=${deviceId}&api_key=${encodeURIComponent(auth.token)}&PlaySessionId=${encodeURIComponent(info.PlaySessionId)}${src.ETag ? '&Tag=' + encodeURIComponent(src.ETag) : ''}`;
      } else if (src.SupportsTranscoding && src.TranscodingUrl) {
        method = 'Transcode'; url = base + src.TranscodingUrl;
      } else throw new Error('This file cannot be played in the browser.');
      this.session = { itemId: a.ItemId, mediaSourceId: src.Id, playSessionId: info.PlaySessionId, method, audioIndex: audioIndex ?? src.DefaultAudioStreamIndex, subIndex: subIndex ?? null, src, startTicks, runtimeTicks: src.RunTimeTicks || 0 };
      // subtitles
      $$('track', v).forEach((t) => t.remove());
      const textSubs = (src.MediaStreams || []).filter((s) => s.Type === 'Subtitle' && (s.IsTextSubtitleStream || s.SupportsExternalStream));
      const prefLang = store.get('jellyschedule.subLang');
      textSubs.forEach((s) => {
        const t = document.createElement('track');
        t.kind = 'subtitles'; t.label = s.DisplayTitle || s.Language || 'Subtitles'; t.srclang = (s.Language || '').slice(0, 2); t.dataset.index = s.Index;
        t.src = `${base}/Videos/${a.ItemId}/${encodeURIComponent(src.Id)}/Subtitles/${s.Index}/0/Stream.vtt?api_key=${encodeURIComponent(auth.token)}`;
        t.default = false; v.appendChild(t);
      });
      const wantSub = subIndex != null ? subIndex : (prefLang ? (textSubs.find((s) => (s.Language || '') === prefLang) || {}).Index : null);
      if (method === 'Transcode' && /^https?:/.test(url) === false && url.includes('.m3u8')) { /* relative */ }
      const isHls = method === 'Transcode' && (src.TranscodingSubProtocol === 'hls' || url.includes('.m3u8'));
      const onReady = () => {
        if (startTicks > 0) { try { v.currentTime = startTicks / 1e7; } catch { /* ignore */ } }
        if (wantSub != null) this.selectSub(wantSub, true);
        v.play().catch(() => { $('#pCenter', this.root).innerHTML = `<div class="bigplay" id="bigPlay">${ICONS.play}</div>`; $('#bigPlay', this.root).addEventListener('click', () => { v.play(); $('#pCenter', this.root).innerHTML = ''; }); });
      };
      if (isHls && !v.canPlayType('application/vnd.apple.mpegurl')) {
        await this.loadHls();
        if (!window.Hls || !window.Hls.isSupported()) throw new Error('HLS playback is not supported in this browser.');
        this.hls = new window.Hls({ maxBufferLength: 30, backBufferLength: 60, startPosition: startTicks > 0 ? startTicks / 1e7 : -1 });
        this.hls.on(window.Hls.Events.ERROR, (_, data) => { if (data.fatal) this.onError('Streaming error: ' + (data.details || data.type)); });
        this.hls.on(window.Hls.Events.MANIFEST_PARSED, () => onReady());
        this.hls.loadSource(url); this.hls.attachMedia(v);
      } else {
        v.src = url;
        v.addEventListener('loadedmetadata', onReady, { once: true });
        v.load();
      }
      await this.report('start');
      clearInterval(this.progressTimer); this.progressTimer = setInterval(() => this.report('progress'), 10000);
      clearInterval(this.mirrorTimer); if (householdMismatch()) this.mirrorTimer = setInterval(() => this.mirror(false), 30000);
    } catch (e) {
      console.error(e);
      this.onError(e.message || String(e), !forceTranscode);
    }
  },

  loadHls() {
    if (window.Hls) return Promise.resolve();
    return new Promise((resolve, reject) => { const s = document.createElement('script'); s.src = `${base}/JellySchedule/app/hls.min.js`; s.onload = resolve; s.onerror = () => reject(new Error('Could not load HLS player')); document.head.appendChild(s); });
  },

  positionTicks() { return Math.floor((this.video.currentTime || 0) * 1e7); },

  async report(kind) {
    const s = this.session; if (!s) return;
    const v = this.video;
    const body = { ItemId: s.itemId, MediaSourceId: s.mediaSourceId, PlaySessionId: s.playSessionId, PositionTicks: this.positionTicks(), CanSeek: true, IsPaused: v.paused, IsMuted: v.muted, VolumeLevel: Math.round(v.volume * 100), PlayMethod: s.method, AudioStreamIndex: s.audioIndex, SubtitleStreamIndex: s.subIndex, RepeatMode: 'RepeatNone', PlaybackOrder: 'Default' };
    try {
      if (kind === 'start') await jf('Sessions/Playing', { method: 'POST', body });
      else if (kind === 'stop') { await jf('Sessions/Playing/Stopped', { method: 'POST', body: { ItemId: s.itemId, MediaSourceId: s.mediaSourceId, PlaySessionId: s.playSessionId, PositionTicks: this.positionTicks() } }); if (s.method === 'Transcode') jf(`Videos/ActiveEncodings?deviceId=${deviceId}&playSessionId=${encodeURIComponent(s.playSessionId)}`, { method: 'DELETE' }).catch(() => {}); }
      else await jf('Sessions/Playing/Progress', { method: 'POST', body });
    } catch (e) { console.warn('report failed', e); }
  },

  async mirror(final) {
    if (!householdMismatch() || !this.session) return;
    const pos = this.positionTicks(); const dur = this.session.runtimeTicks || (this.video.duration ? this.video.duration * 1e7 : 0);
    const played = final && dur > 0 && pos / dur >= 0.9;
    try { await js('playstate', { method: 'POST', body: { ItemId: this.session.itemId, PositionTicks: played ? 0 : pos, Played: played ? true : undefined } }); } catch { /* ignore */ }
  },

  updateTime() {
    const v = this.video; if (!v.duration) return;
    $('#pFill', this.root).style.width = `${(v.currentTime / v.duration * 100).toFixed(2)}%`;
    try { const b = v.buffered; if (b.length) { const end = b.end(b.length - 1); $('#pBuf', this.root).style.left = `${(b.start(b.length - 1) / v.duration * 100).toFixed(2)}%`; $('#pBuf', this.root).style.width = `${((end - b.start(b.length - 1)) / v.duration * 100).toFixed(2)}%`; } } catch { /* ignore */ }
    $('#pTime', this.root).textContent = `${fmtSec(v.currentTime)} / ${fmtSec(v.duration)}`;
    if (this.opts && this.opts.live && state.guide && state.guide.UpNext && this.airing && state.guide.UpNext.Id !== this.airing.Id) $('#pNext', this.root).textContent = `Next: ${airingTitle(state.guide.UpNext)} at ${wall(state.guide.UpNext.Start).hm}`;
  },

  togglePlay() { const v = this.video; if (v.paused) v.play().catch(() => {}); else v.pause(); },
  seekBy(s) { const v = this.video; v.currentTime = Math.max(0, Math.min((v.duration || 1e9), v.currentTime + s)); this.report('progress'); },
  toggleFullscreen() { if (document.fullscreenElement) document.exitFullscreen().catch(() => {}); else this.root.requestFullscreen && this.root.requestFullscreen().catch(() => {}); },
  wake() { this.root.classList.remove('idle'); clearTimeout(this.idleTimer); this.idleTimer = setTimeout(() => { if (!this.video.paused && !this.menuOpen) this.root.classList.add('idle'); }, 3000); },
  onKey(e) {
    if (!this.active) return;
    if (e.target && /input|select|textarea/i.test(e.target.tagName)) return;
    switch (e.key) {
      case ' ': case 'k': e.preventDefault(); this.togglePlay(); break;
      case 'ArrowLeft': e.preventDefault(); this.seekBy(-10); break;
      case 'ArrowRight': e.preventDefault(); this.seekBy(10); break;
      case 'ArrowUp': e.preventDefault(); this.video.volume = Math.min(1, this.video.volume + 0.1); break;
      case 'ArrowDown': e.preventDefault(); this.video.volume = Math.max(0, this.video.volume - 0.1); break;
      case 'f': this.toggleFullscreen(); break;
      case 'c': this.toggleMenu('sub'); break;
      case 'm': this.video.muted = !this.video.muted; break;
      case 'Escape': if (this.menuOpen) this.closeMenu(); else if (document.fullscreenElement) document.exitFullscreen().catch(() => {}); else this.close(); break;
      default: return;
    }
    this.wake();
  },

  toggleMenu(kind) {
    if (this.menuOpen === kind) { this.closeMenu(); return; }
    this.closeMenu();
    const s = this.session; if (!s) return;
    const menu = el('<div class="menu" id="pMenu"></div>');
    if (kind === 'sub') {
      const tracks = $$('track', this.video);
      menu.appendChild(el('<div class="mh">Subtitles</div>'));
      const off = el(`<div class="mi ${s.subIndex == null ? 'on' : ''}">Off</div>`); off.addEventListener('click', () => { this.selectSub(null); this.closeMenu(); }); menu.appendChild(off);
      if (!tracks.length) menu.appendChild(el('<div class="mi muted">No text subtitles in this file</div>'));
      tracks.forEach((t) => { const mi = el(`<div class="mi ${String(s.subIndex) === t.dataset.index ? 'on' : ''}">${h(t.label)}</div>`); mi.addEventListener('click', () => { this.selectSub(+t.dataset.index); this.closeMenu(); }); menu.appendChild(mi); });
    } else {
      const streams = (s.src.MediaStreams || []).filter((x) => x.Type === 'Audio');
      menu.appendChild(el('<div class="mh">Audio</div>'));
      streams.forEach((x) => { const mi = el(`<div class="mi ${s.audioIndex === x.Index ? 'on' : ''}">${h(x.DisplayTitle || x.Language || 'Audio ' + x.Index)}</div>`); mi.addEventListener('click', async () => { this.closeMenu(); if (x.Index !== s.audioIndex) { const pos = this.positionTicks(); await this.report('stop'); await this.start({ startTicks: pos, audioIndex: x.Index, subIndex: s.subIndex }); } }); menu.appendChild(mi); });
      if (streams.length < 2) menu.appendChild(el('<div class="mi muted">Only one audio track</div>'));
      const tc = el(`<div class="mi">${s.method === 'Transcode' ? 'Playing via transcode' : 'Force transcoding (fix stutter / codec issues)'}</div>`);
      if (s.method !== 'Transcode') tc.addEventListener('click', async () => { this.closeMenu(); const pos = this.positionTicks(); await this.report('stop'); await this.start({ startTicks: pos, audioIndex: s.audioIndex, subIndex: s.subIndex, forceTranscode: true }); });
      menu.appendChild(tc);
    }
    $('.overlay', this.root).appendChild(menu); this.menuOpen = kind; this.wake();
  },
  closeMenu() { const m = $('#pMenu', this.root); if (m) m.remove(); this.menuOpen = null; },
  selectSub(index, silent) {
    const tracks = $$('track', this.video);
    let lang = null;
    tracks.forEach((t) => { const on = index != null && String(index) === t.dataset.index; t.track.mode = on ? 'showing' : 'disabled'; if (on) lang = (this.session.src.MediaStreams.find((s) => s.Index === index) || {}).Language || ''; });
    if (this.session) this.session.subIndex = index;
    if (!silent) store.set('jellyschedule.subLang', index == null ? null : lang);
  },

  async onEnded() {
    if (this.ended) return; this.ended = true;
    clearInterval(this.progressTimer); clearInterval(this.mirrorTimer);
    await this.report('stop'); await this.mirror(true);
    if (this.opts && this.opts.live) await this.nextProgramme();
    else if (this.opts && this.opts.recording) { this.close(); toast('Recording finished'); }
    else this.close();
  },

  async nextProgramme() {
    let n;
    try { n = await js('now'); } catch { this.close(); return; }
    const cur = this.airing;
    if (n.OnNow && n.OnNow.ItemId !== cur.ItemId) { await this.playAiring(n.OnNow, { live: true }); return; }
    const next = n.UpNext;
    const inter = $('#pInter', this.root);
    if (next && ms(next.Start) - ms(serverNow()) <= 3 * 3600000) {
      const relaxed = settings().LiveMode !== 'Strict';
      inter.className = 'interstitial'; inter.classList.remove('hidden');
      inter.innerHTML = `<div class="card">${posterHtml(airingPoster(next, 300), '', airingTitle(next))}<div class="muted small">UP NEXT · ${wall(next.Start).hm}</div><h2>${h(airingTitle(next))}</h2><div class="muted">${h(airingSub(next))}</div><div class="count" id="iCount"></div><div class="actions">${relaxed ? `<button class="btn primary" id="iNow">${ICONS.play} Start now</button>` : ''}<button class="btn" id="iGuide">Back to guide</button></div></div>`;
      const startIt = () => { clearInterval(t); this.playAiring(next, { live: true }); };
      const tick = () => { const left = Math.max(0, Math.round((ms(next.Start) - ms(serverNow())) / 1000)); $('#iCount', inter).textContent = left > 0 ? fmtSec(left) : 'Starting…'; if (left <= 0) startIt(); };
      const t = setInterval(tick, 1000); tick();
      const nowBtn = $('#iNow', inter); if (nowBtn) nowBtn.addEventListener('click', startIt);
      $('#iGuide', inter).addEventListener('click', () => { clearInterval(t); this.close(); });
    } else {
      inter.className = 'interstitial'; inter.classList.remove('hidden');
      inter.innerHTML = `<div class="card"><div class="tv-static">Off air</div><h2>That's all for tonight</h2><div class="muted">${n.NextWindowStart ? `Back on ${h(relDate(n.NextWindowStart, serverNow()))}` : 'Nothing else is scheduled.'}</div><div class="actions"><button class="btn primary" id="iGuide">Back to guide</button></div></div>`;
      $('#iGuide', inter).addEventListener('click', () => this.close());
    }
  },

  onError(msg, canRetry) {
    $('#pCenter', this.root).innerHTML = '';
    const box = $('#pErr', this.root); box.className = 'err'; box.classList.remove('hidden');
    box.innerHTML = `<div class="card"><h3>Couldn't play this</h3><p class="muted">${h(msg)}</p><div class="row" style="justify-content:center">${canRetry ? `<button class="btn primary" id="eRetry">Try transcoding</button>` : ''}<button class="btn" id="eClose">Close</button></div></div>`;
    const r = $('#eRetry', box); if (r) r.addEventListener('click', () => { box.classList.add('hidden'); this.start({ startTicks: this.session ? this.positionTicks() : 0, forceTranscode: true }); });
    $('#eClose', box).addEventListener('click', () => this.close());
  },

  teardownMedia() {
    if (this.hls) { try { this.hls.destroy(); } catch { /* ignore */ } this.hls = null; }
    const v = this.video; try { v.pause(); } catch { /* ignore */ } v.removeAttribute('src'); $$('track', v).forEach((t) => t.remove()); try { v.load(); } catch { /* ignore */ }
  },

  async close() {
    if (!this.active) return;
    clearInterval(this.progressTimer); clearInterval(this.mirrorTimer);
    if (this.session && !this.ended) { await this.report('stop'); await this.mirror(false); }
    this.teardownMedia(); this.session = null; this.active = false; this.closeMenu();
    $('#pInter', this.root).classList.add('hidden'); $('#pErr', this.root).classList.add('hidden');
    this.root.classList.add('hidden'); document.body.style.overflow = '';
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    route();
  }
};

// ============================================================ boot
async function boot() {
  try {
    await loadState();
  } catch (e) {
    if (e.status === 401 || e.status === 403) { showLogin(); return; }
    $('#main').innerHTML = `<div class="empty"><h3>Could not reach Jelly Schedule</h3><p>${h(e.message)}</p></div>`; return;
  }
  await route();
  // Autoplay offer: if something is on right now and autoplay is enabled, jump straight in (a click is still required by browsers).
  if (settings().AutoplayOnOpen && currentView() === 'guide' && state.guide && state.guide.OnNow && !sessionStorage.getItem('js.autoplayed')) {
    sessionStorage.setItem('js.autoplayed', '1');
    const a = state.guide.OnNow;
    const m = modal(`<div class="modal-head"><span class="onair-pill"><span class="dot"></span> ON NOW</span><h2>${h(airingTitle(a))}</h2><button class="btn icon ghost" data-close>${ICONS.close}</button></div><p class="muted">${h(airingSub(a))} · until ${wall(a.End).hm}</p><div class="modal-foot"><button class="btn" data-close>Browse the guide</button><button class="btn primary lg" id="ap">${ICONS.play} Tune in</button></div>`);
    $('#ap', m.box).addEventListener('click', () => { m.close(); Player.playAiring(a, { live: true }); });
  }
}

window.addEventListener('resize', () => { clearTimeout(window.__rs); window.__rs = setTimeout(() => { if (currentView() === 'guide' && !Player.active) route(); }, 300); });
document.addEventListener('DOMContentLoaded', async () => {
  if (await tryRestoreAuth()) await boot(); else showLogin();
});
})();
