// End-to-end UI check against the DevHost (tools/DevHost). Usage:
//   NODE_PATH=/opt/node22/lib/node_modules node tools/ui-test/ui-test.mjs http://127.0.0.1:5088 ./screenshots
import { createRequire } from 'node:module';
import fs from 'node:fs';
const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const base = process.argv[2] || 'http://127.0.0.1:5088';
const out = process.argv[3] || './screenshots';
fs.mkdirSync(out, { recursive: true });
const failures = [];
const check = (cond, msg) => { if (!cond) { failures.push(msg); console.log('  ✗', msg); } else console.log('  ✓', msg); };
const shot = (page, name) => page.screenshot({ path: `${out}/${name}.png`, fullPage: false });

// ---- seed a known state through the API (DevHost exposes /dev/* helpers)
async function seed() {
  const hdr = { 'Content-Type': 'application/json', Authorization: 'MediaBrowser Client="t", Device="t", DeviceId="t", Version="1"' };
  const login = await (await fetch(`${base}/Users/AuthenticateByName`, { method: 'POST', headers: hdr, body: JSON.stringify({ Username: 'household', Pw: '' }) })).json();
  const H = { ...hdr, Authorization: hdr.Authorization + `, Token="${login.AccessToken}"` };
  const api = (path, method = 'GET', body) => fetch(`${base}/${path}`, { method, headers: H, body: body === undefined ? undefined : JSON.stringify(body) }).then((r) => r.status === 204 ? null : r.json());
  await fetch(`${base}/dev/reset`, { method: 'POST' });
  const items = Object.fromEntries((await (await fetch(`${base}/dev/items`)).json()).map((i) => [i.Name, i.Id]));
  const now = new Date();
  const hm = (d) => `${String(d.getUTCHours()).padStart(2, '0')}:${String(d.getUTCMinutes()).padStart(2, '0')}`;
  const todayName = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][now.getUTCDay()];
  const windows = [
    { Days: ['Tuesday', 'Thursday'], Start: '20:00', End: '22:00' },
    { Days: ['Sunday'], Start: '19:00', End: '22:30', Label: 'Sunday night' }
  ];
  const tonight = { Days: [todayName], Start: hm(new Date(now.getTime() - 20 * 60000)), End: hm(new Date(now.getTime() + 100 * 60000)), Label: 'tonight' };
  await api('JellySchedule/windows', 'PUT', windows);
  for (const [name, mode, days] of [['The Bear', 'InOrder', ['Tuesday']], ['Severance', 'InOrder', []], ['Slow Horses', 'InOrder', []], ['Seinfeld', 'ReRun', []], ['Frasier', 'ReRun', []], ['Sunday Blockbusters', 'InOrder', []], ['Arrival', 'InOrder', []]]) {
    await api('JellySchedule/lineup', 'POST', { ItemId: items[name], Mode: mode, Days: days, EpisodesPerAiring: 1 });
  }
  await api('JellySchedule/movie-night', 'PUT', { Days: ['Sunday'], Position: 'Start', Order: 'AsAdded' });
  const ep1 = await (await fetch(`${base}/dev/episode?series=The%20Bear&season=1&episode=1`)).json();
  const ep2 = await (await fetch(`${base}/dev/episode?series=The%20Bear&season=1&episode=2`)).json();
  await api('JellySchedule/playstate', 'POST', { ItemId: ep1.Id, Played: true });
  await api('JellySchedule/recordings', 'POST', { ItemId: ep2.Id });
  console.log('seeded');
  return { addTonight: () => api('JellySchedule/windows', 'PUT', windows.concat([tonight])) };
}
const seeded = await seed();

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || undefined, args: ['--autoplay-policy=no-user-gesture-required', '--mute-audio'] });
try {
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

  console.log('1. login');
  await page.goto(`${base}/JellySchedule/app`);
  await page.waitForSelector('#loginForm', { timeout: 15000 });
  await shot(page, '00-login');
  await page.fill('#u', 'household');
  await page.fill('#p', 'x');
  await page.click('#loginForm button[type=submit]');
  await page.waitForSelector('.hero', { timeout: 20000 });
  // next week: a full lineup week for the README screenshot (before the temporary "tonight" window exists)
  await page.click('#nextWeek');
  await page.waitForSelector('.week-nav');
  await page.waitForTimeout(700);
  check((await page.$$('.prog-block')).length >= 6, `next week has a full schedule (${(await page.$$('.prog-block')).length} blocks)`);
  await page.evaluate(() => { const sc = document.querySelector('.guide-scroll'); if (sc) sc.scrollTop = 0; });
  await page.setViewportSize({ width: 1400, height: 1180 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${out}/01b-guide-next-week.png` });
  await page.setViewportSize({ width: 1400, height: 900 });

  // now add a window that is on air right now and reload the guide
  await seeded.addTonight();
  await page.click('#thisWeek');
  await page.waitForSelector('.hero', { timeout: 20000 });
  const offer = await page.$('.modal-bg');
  if (offer) { await page.click('.modal-bg [data-close]'); }
  await page.waitForTimeout(600);
  check(await page.$('.ggrid'), 'guide grid rendered');
  const blocks = await page.$$('.prog-block');
  check(blocks.length > 3, `programme blocks present (${blocks.length})`);
  await shot(page, '01-guide');

  console.log('2. programme detail');
  await page.click('.prog-block');
  await page.waitForSelector('.modal .detail');
  await page.waitForTimeout(400);
  await shot(page, '02-detail');
  check(await page.$('#dPlay'), 'detail has play button');
  await page.click('.modal [data-close]');

  console.log('3. lineup');
  await page.click('a[data-view=lineup]');
  await page.waitForSelector('.show-card', { timeout: 15000 });
  const cards = await page.$$('.show-card');
  check(cards.length >= 5, `lineup cards (${cards.length})`);
  await shot(page, '03-lineup');
  // add a movie through the UI
  await page.click('#addMovie');
  await page.waitForSelector('#q');
  await page.fill('#q', 'Knives');
  await page.waitForSelector('#results .result:has-text("Knives Out")', { timeout: 10000 });
  await page.click('#results .result:has-text("Knives Out")');
  await page.waitForSelector('#step2 #save');
  await page.waitForTimeout(400);
  await shot(page, '04-add-movie');
  await page.click('#step2 #save');
  await page.waitForSelector('.toast');
  await page.waitForTimeout(800);
  const html = await page.content();
  check(html.includes('Knives Out'), 'Knives Out added to movie night');
  // edit a show
  await page.click('.show-card [data-act=edit]');
  await page.waitForSelector('#modeSeg');
  await page.waitForTimeout(400);
  await shot(page, '05-edit-show');
  await page.click('.modal [data-close]');

  console.log('4. schedule');
  await page.click('a[data-view=schedule]');
  await page.waitForSelector('.window', { timeout: 15000 });
  check((await page.$$('.window')).length >= 2, 'viewing windows listed');
  check(await page.$('.weekpreview'), 'week preview rendered');
  await shot(page, '06-schedule');

  console.log('5. recordings + player');
  await page.click('a[data-view=recordings]');
  await page.waitForSelector('.rec-card', { timeout: 15000 });
  await shot(page, '07-recordings');
  await page.click('.rec-card [data-act=play]');
  await page.waitForSelector('.player:not(.hidden) video');
  await page.waitForFunction(() => { const v = document.querySelector('.player video'); return v && v.currentTime > 1.5; }, null, { timeout: 20000 }).catch(() => {});
  const t = await page.evaluate(() => document.querySelector('.player video').currentTime);
  const perr = await page.$eval('#pErr', (e) => e.classList.contains('hidden') ? '' : e.textContent.trim()).catch(() => '');
  check(t > 1, `video is playing (t=${t.toFixed(1)}s)${perr ? ' — player error: ' + perr : ''}`);
  if (perr) { await shot(page, '08-player-error'); await page.click('#eClose'); await page.waitForSelector('.player.hidden', { state: 'attached' }); }
  if (!perr) {
    await page.mouse.move(700, 450);
    await page.waitForTimeout(300);
    await shot(page, '08-player');
    await page.keyboard.press('c');
    await page.waitForSelector('#pMenu');
    check((await page.$$('#pMenu .mi')).length >= 2, 'subtitle menu lists tracks');
    await page.click('#pMenu .mi:nth-child(3)');
    await page.waitForTimeout(300);
    const cueMode = await page.evaluate(() => Array.from(document.querySelectorAll('.player video track')).map((t) => t.track.mode).join(','));
    check(cueMode.includes('showing'), `subtitle track enabled (${cueMode})`);
    await page.keyboard.press('Escape');
    await page.waitForSelector('.player.hidden', { state: 'attached', timeout: 10000 });
    check(true, 'player closed');
  }

  console.log('6. settings');
  await page.click('a[data-view=settings]');
  await page.waitForSelector('#saveSettings', { timeout: 15000 });
  await shot(page, '09-settings');
  await page.click('#saveSettings');
  await page.waitForSelector('.toast');

  console.log('7. guide: tune in to what is on now');
  await page.click('a[data-view=guide]');
  await page.waitForSelector('.hero', { timeout: 15000 });
  const tune = await page.$('#tuneIn');
  check(tune, 'something is on now (tune in button)');
  if (tune) {
    await tune.click();
    await page.waitForSelector('.player:not(.hidden) video');
    await page.waitForFunction(() => { const v = document.querySelector('.player video'); return v && v.currentTime > 1.5; }, null, { timeout: 20000 }).catch(() => {});
    const live = await page.$eval('#pBug', (e) => e.textContent);
    check(/LIVE/.test(live), 'live bug shown');
    await page.mouse.move(700, 450);
    await shot(page, '10-live-player');
    // jump to the end to trigger the up-next interstitial
    await page.evaluate(() => { const v = document.querySelector('.player video'); v.currentTime = v.duration - 0.5; });
    await page.waitForSelector('.interstitial', { timeout: 20000 }).catch(() => {});
    check(await page.$('.interstitial'), 'up-next / off-air interstitial after the programme ends');
    await shot(page, '11-interstitial');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
  }

  console.log('8. mobile layout');
  const mctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  const mp = await mctx.newPage();
  await mp.goto(`${base}/JellySchedule/app`);
  await mp.waitForSelector('#loginForm');
  await mp.fill('#u', 'household'); await mp.click('#loginForm button[type=submit]');
  await mp.waitForSelector('.hero', { timeout: 20000 });
  const moffer = await mp.$('.modal-bg'); if (moffer) await mp.click('.modal-bg [data-close]');
  await mp.waitForTimeout(500);
  check(await mp.$('.daytabs'), 'mobile day tabs rendered');
  await shot(mp, '12-mobile-guide');
  await mp.click('a[data-view=lineup]');
  await mp.waitForSelector('.show-card');
  await shot(mp, '13-mobile-lineup');

  console.log('errors:', errors.length ? errors : 'none');
  errors.forEach((e) => failures.push('console: ' + e));
} finally {
  await browser.close();
}
console.log(failures.length ? `FAILED: ${failures.length}` : 'ALL CHECKS PASSED');
process.exit(failures.length ? 1 : 0);
