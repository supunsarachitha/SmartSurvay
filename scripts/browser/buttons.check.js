// Headless-browser GUI check of the buttons (real Blazor circuits, not just HTTP):
//  1. Scenarios that check what the key buttons do: Preview from the survey list, the builder (saved and with unsaved
//     changes) and the share page; preview width toggle; runner Next/Back/Submit/Restart; builder editing buttons;
//     status menu; new survey; duplicate and delete with confirmation; exports (definition, responses, reports); copy
//     and QR buttons; print; report live preview; user dialog; branding live preview; theme toggle; public runner.
//  2. A crawler that clicks every visible button and button-styled link on every page for each role (guest, member,
//     admin, super admin) and reports clicks that raise errors or change nothing. Account-ending buttons (log out,
//     delete account, 2FA resets) are never clicked; dialogs a click opens are cancelled.
// Collects console errors, failed requests, 5xx responses and Blazor's error bar.
//
// Run against a FRESH instance with demo data (it creates, edits and deletes data), preferably a throwaway container of
// the real image — Development builds serve Blazor's scripts differently from the published app:
//   docker compose build web && docker network create ss-gui && \
//   docker run -d --rm --name ss-gui-db --network ss-gui -e POSTGRES_USER=t -e POSTGRES_PASSWORD=t -e POSTGRES_DB=t postgres:16-alpine && \
//   docker run -d --rm --name ss-gui-web --network ss-gui -p 8099:8080 -e Seed__DemoData=true -e Seed__AdminPassword='Admin123!' \
//     -e Seed__SuperAdminPassword='SuperAdmin123!' -e RateLimits__AuthPerMinute=500 \
//     -e ConnectionStrings__DefaultConnection='Host=ss-gui-db;Database=t;Username=t;Password=t' smartsurvey:latest
//   cd scripts/browser && npm install && BASE=http://localhost:8099 npm run buttons   # CHROME_PATH=… to override
//   docker stop ss-gui-web ss-gui-db && docker network rm ss-gui                      # afterwards
// (A local SQLite instance as described in workspaces.check.js works too; BASE defaults to http://localhost:5230.)
// Exit code 0 when every check passed and the browser reported no errors.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'http://localhost:5230';
const CHROME = process.env.CHROME_PATH || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const ADMIN = ['admin@smartsurvey.local', process.env.ADMIN_PASSWORD || 'Admin123!'];
const SUPER = ['superadmin@smartsurvey.local', process.env.SUPERADMIN_PASSWORD || 'SuperAdmin123!'];
const MEMBER = ['user@smartsurvey.local', process.env.USER_PASSWORD || 'User123!'];
const results = []; const errors = []; const notes = [];
const check = (name, ok, info = '') => { results.push([ok ? 'OK  ' : 'FAIL', name, info]); return ok; };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const GUID = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

// Never clicked by the crawler: they end the session or destroy the account.
const NEVER = /log ?out|sign ?out|delete data|close my account|disable 2fa|reset authenticator|forget this browser|recovery codes/i;
// Expected to ask for confirmation first (the crawler cancels the dialog; no dialog is noted, not failed).
const DANGER = /\b(delete|remove|disable|reject|archive|close survey|revoke)\b|reset to defaults/i;

// Records downloads, print calls, clipboard writes and window.open instead of performing them.
function instrument() {
  window.__gui = { downloads: [], prints: 0, copies: [], opens: [], blob: 0 };
  const createUrl = URL.createObjectURL;
  URL.createObjectURL = b => { window.__gui.blob = b && b.size || 0; return createUrl.call(URL, b); };
  const click = HTMLAnchorElement.prototype.click;
  HTMLAnchorElement.prototype.click = function () {
    if (this.hasAttribute('download')) { window.__gui.downloads.push({ name: this.download, size: window.__gui.blob }); return; }
    return click.call(this);
  };
  window.print = () => { window.__gui.prints++; };
  window.open = u => { window.__gui.opens.push(String(u)); return null; };
  try { navigator.clipboard.writeText = t => { window.__gui.copies.push(String(t)); return Promise.resolve(); }; } catch { /* no clipboard */ }
}

async function newPage(browser, role) {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  await page.setViewport({ width: 1366, height: 900 });
  page.setDefaultTimeout(15000);
  await page.evaluateOnNewDocument(instrument);
  const where = () => `[${role}] ${page.url().replace(BASE, '')}`;
  page.on('console', m => { if (m.type() === 'error') errors.push(`${where()} console: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${where()} pageerror: ${e.message}`));
  page.on('requestfailed', r => {
    const f = r.failure()?.errorText || '';
    if (!r.url().includes('_blazor') && !r.url().includes('/_framework/') && !f.includes('ERR_ABORTED')) errors.push(`${where()} requestfailed: ${r.url()} ${f}`);
  });
  page.on('response', r => { if (r.status() >= 500) errors.push(`${where()} HTTP ${r.status()} ${r.url()}`); });
  page.on('dialog', d => d.accept()); // "leave with unsaved changes?" prompts
  page.role = role;
  return page;
}
const go = async (page, path) => { await page.goto(BASE + path, { waitUntil: 'networkidle0' }); await sleep(1300); };
const text = page => page.evaluate(() => document.body.innerText);
const gui = page => page.evaluate(() => window.__gui);
async function login(page, [email, password]) {
  await go(page, '/Account/Login');
  await page.type('#login-email', email); await page.type('#login-password', password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
  await sleep(800);
}
// Clicks the first visible element matching selector whose text (or aria-label/title) contains t.
async function clickText(page, selector, t) {
  for (const h of await page.$$(selector)) {
    const ok = await h.evaluate((e, t) => {
      const r = e.getBoundingClientRect();
      const s = `${e.innerText} ${e.getAttribute('aria-label') || ''} ${e.getAttribute('title') || ''}`;
      return r.width > 0 && r.height > 0 && s.includes(t);
    }, t);
    if (ok) { await h.click(); return; }
  }
  throw new Error(`no visible ${selector} containing "${t}" on ${page.url()}`);
}
// Interactive pages ignore input until the circuit is connected: retry an action until the page reacts.
async function untilReacts(page, action, reacted, what) {
  for (let attempt = 0; attempt < 6; attempt++) {
    await action();
    for (let i = 0; i < 10; i++) { await sleep(200); if (await reacted()) return true; }
  }
  throw new Error(`page did not react: ${what} on ${page.url()}`);
}
const count = (page, selector) => page.$$eval(selector, es => es.length);
const toastText = page => page.$$eval('.toast-item', es => es.map(e => e.innerText).join(' | '));
async function waitFor(fn, ms = 8000) {
  const end = Date.now() + ms;
  while (Date.now() < end) { if (await fn()) return true; await sleep(200); }
  return false;
}

// Answers every visible question on the current runner page with the first choice / a short text.
async function answerPage(page) {
  await page.evaluate(() => {
    for (const card of document.querySelectorAll('.question-card')) {
      if (!card.getBoundingClientRect().height) continue;
      const radio = card.querySelector('input[type=radio]'); if (radio && !card.querySelector('input[type=radio]:checked')) radio.click();
      const box = card.querySelector('input[type=checkbox]'); if (box && !card.querySelector('input[type=checkbox]:checked')) box.click();
      const star = card.querySelector('.rating-stars button, .scale-options button'); if (star && !card.querySelector('[aria-checked=true]')) star.click();
      const sel = card.querySelector('select'); if (sel && sel.selectedIndex < 1 && sel.options.length > 1) { sel.selectedIndex = 1; sel.dispatchEvent(new Event('change', { bubbles: true })); }
    }
  });
  await sleep(300);
  for (const h of await page.$$('.question-card textarea, .question-card input.form-control')) {
    const [empty, type] = await h.evaluate(e => [!e.value, e.type]);
    if (!empty || !(await h.boundingBox())) continue;
    if (type === 'number') await h.type('3');
    else if (type === 'date') await h.evaluate(e => { e.value = '2026-09-01'; e.dispatchEvent(new Event('change', { bubbles: true })); });
    else if (type === 'email') await h.type('gui@example.com');
    else await h.type('GUI test');
    await h.evaluate(e => e.dispatchEvent(new Event('change', { bubbles: true })));
  }
  await sleep(300);
}
// Types the text of the active question in the builder (a question without text blocks saving).
async function fillQuestionText(page, value) {
  const selector = '.question-editor.active textarea[id^="qt-"]';
  await untilReacts(page, async () => {
    await page.$eval(selector, e => { e.value = ''; });
    await page.type(selector, value);
    await page.$eval(selector, e => e.dispatchEvent(new Event('change', { bubbles: true })));
  }, async () => (await page.$eval('.question-editor.active', e => e.innerText)).includes(value.slice(0, 20)), 'question text');
}
const pageIndicator = page => page.evaluate(() => (document.body.innerText.match(/Page \d+ of \d+/) || [''])[0]);
// Answers pages and presses Next until the Submit button shows; returns the number of pages walked.
async function walkToSubmit(page) {
  for (let pages = 1; pages < 15; pages++) {
    await answerPage(page);
    const buttons = await page.$$eval('button', bs => bs.filter(b => b.getBoundingClientRect().height).map(b => b.innerText.trim()));
    if (buttons.some(b => /^Submit/.test(b))) return pages;
    const before = await pageIndicator(page);
    await clickText(page, 'button', 'Next');
    await waitFor(async () => (await pageIndicator(page)) !== before, 5000);
    await sleep(300);
  }
  throw new Error('never reached the Submit button');
}

// ---------------------------------------------------------------------------------------------------------------
// Crawler: clicks every visible button / button-styled link of a page once and checks what happened.
const CLICKABLE = 'button, a.btn, a.dropdown-item, [role=button]';
function describeClickables(selector) {
  const visible = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden'; };
  return [...document.querySelectorAll(selector)].map((e, i) => {
    const text = (e.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 60);
    const label = e.getAttribute('aria-label') || e.getAttribute('title') || '';
    const menu = e.closest('.dropdown-menu');
    const toggle = menu ? menu.parentElement.querySelector('[data-bs-toggle=dropdown]') : null;
    return {
      i, tag: e.tagName, text, label, href: e.tagName === 'A' ? e.getAttribute('href') || '' : '', abs: e.tagName === 'A' ? e.href : '',
      target: e.getAttribute('target') || '', danger: /danger/.test(e.className), disabled: !!e.disabled,
      visible: visible(e), inModal: !!e.closest('.ss-modal'), hiddenInMenu: !!menu && !visible(e),
      idle: e.classList.contains('active') || !!e.closest('.page-item.disabled, .page-item.active'),
      toggle: toggle ? [...document.querySelectorAll(selector)].indexOf(toggle) : -1,
    };
  });
}
function snapshotPage() {
  let h = 0; const s = document.body.innerHTML + '|' + document.documentElement.getAttribute('data-bs-theme');
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0;
  const g = window.__gui || { downloads: [], copies: [], opens: [], prints: 0 };
  const errorBar = document.getElementById('blazor-error-ui');
  const reconnect = document.querySelector('#components-reconnect-modal[open], .components-reconnect-show, .components-reconnect-failed, .components-reconnect-rejected');
  return {
    url: location.href, h, dl: g.downloads.length, cp: g.copies.length, op: g.opens.length, pr: g.prints,
    modal: !!document.querySelector('.ss-modal'),
    broken: (errorBar && getComputedStyle(errorBar).display !== 'none') || !!reconnect,
  };
}
const same = (a, b) => a.url === b.url && a.h === b.h && a.dl === b.dl && a.cp === b.cp && a.op === b.op && a.pr === b.pr;
async function effectOf(page, before, ms = 2200) {
  const clicked = Date.now(), end = clicked + ms;
  while (Date.now() < end) {
    await sleep(150);
    const now = await page.evaluate(snapshotPage).catch(() => null);
    if (now && !same(before, now)) {
      if (now.url !== before.url) { await page.waitForNetworkIdle({ idleTime: 400, timeout: 8000 }).catch(() => {}); await sleep(500); }
      // A click can cause several renders (an open menu closing at once, the dialog after a server round trip): wait at
      // least 700 ms after the click and until the page has been stable for 450 ms.
      let last = now, stableSince = Date.now();
      while (Date.now() < clicked + 5000) {
        await sleep(150);
        const next = await page.evaluate(snapshotPage).catch(() => last);
        if (!same(last, next)) { last = next; stableSince = Date.now(); continue; }
        if (Date.now() - stableSince >= 450 && Date.now() - clicked >= 700) break;
      }
      return last;
    }
  }
  return await page.evaluate(snapshotPage).catch(() => before);
}
async function closeModal(page) {
  for (const label of ['Cancel', 'Close', 'Keep']) {
    const h = await page.evaluateHandle(l => [...document.querySelectorAll('.ss-modal button')].find(b =>
      (b.innerText.trim() === l || b.getAttribute('aria-label') === l) && b.getBoundingClientRect().height), label);
    if (h.asElement()) { await h.asElement().click(); await sleep(500); break; }
  }
  if (await page.$('.ss-modal')) { await page.keyboard.press('Escape'); await sleep(400); }
  return !(await page.$('.ss-modal'));
}
const signature = c => `${c.tag}|${c.text}|${c.label.replace(/ (for|of) .*/, ' …')}|${c.href.replace(GUID, ':id').replace(/\?.*/, '')}`;

async function crawl(page, path, seenLinks, maxClicks = 70) {
  await go(page, path);
  const start = page.url();
  const seen = new Set(); let clicks = 0, noEffect = [], failed = 0, last = '';
  const back = async () => { await page.goto(start, { waitUntil: 'networkidle0' }); await sleep(1200); };
  // Blazor's error bar can show up a moment after the click that caused it.
  const brokenBy = async name => {
    if (!name || !(await page.evaluate(snapshotPage).catch(() => ({}))).broken) return false;
    failed++; check(`[${page.role}] ${path}: click "${name}" leaves the page working`, false, 'Blazor error bar / reconnect dialog');
    return true;
  };
  while (clicks < maxClicks) {
    if (await brokenBy(last)) await back();
    last = '';
    if (page.url() !== start) await back();
    if (await page.$('.ss-modal')) await closeModal(page);
    const all = await page.evaluate(describeClickables, CLICKABLE);
    const next = all.find(c => !c.disabled && !c.idle && !c.inModal && (c.visible || (c.hiddenInMenu && c.toggle >= 0))
      && !seen.has(signature(c)) && !(c.tag === 'A' && seenLinks.has(c.href.replace(GUID, ':id'))));
    if (!next) break;
    seen.add(signature(next));
    const name = `${next.text || next.label || next.href || next.tag}`.slice(0, 50);
    if (NEVER.test(`${next.text} ${next.label}`)) { notes.push(`skip  [${page.role}] ${path} "${name}" (never clicked)`); continue; }
    if (next.tag === 'A') {
      seenLinks.add(next.href.replace(GUID, ':id'));
      const url = next.abs || new URL(next.href, start).href; // resolved against the page's <base href>
      const external = /^https?:/.test(url) && !url.startsWith(BASE);
      if (external || next.target === '_blank') {
        if (url.startsWith(BASE)) {
          const status = await page.evaluate(u => fetch(u, { redirect: 'follow' }).then(r => r.status, () => 0), url);
          check(`[${page.role}] ${path}: "${name}" opens ${url.replace(BASE, '')} in a new tab`, status === 200, `HTTP ${status}`);
        } else notes.push(`link  [${page.role}] ${path} "${name}" → ${url} (external, not opened)`);
        continue;
      }
    }
    clicks++;
    let before = await page.evaluate(snapshotPage);
    try {
      if (next.hiddenInMenu) {
        const toggle = (await page.$$(CLICKABLE))[next.toggle];
        if (await toggle.evaluate(e => e.getAttribute('aria-expanded') !== 'true')) { await toggle.click(); await sleep(350); }
        before = await page.evaluate(snapshotPage); // the item's own effect, not the menu opening
      }
      const handle = (await page.$$(CLICKABLE))[next.i];
      const matches = handle && await handle.evaluate((e, t) => (e.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 60) === t, next.text);
      if (!matches) { notes.push(`moved [${page.role}] ${path} "${name}" (page changed before the click)`); continue; }
      await handle.click();
      last = name;
    } catch (e) { failed++; check(`[${page.role}] ${path}: click "${name}"`, false, e.message.split('\n')[0]); continue; }
    let after = await effectOf(page, before);
    if (same(before, after) && !next.hiddenInMenu) { await sleep(1500); after = await page.evaluate(snapshotPage).catch(() => after); } // slow circuit
    if (await brokenBy(name)) { last = ''; await back(); continue; }
    if (after.url !== before.url) {
      const t = await text(page).catch(() => '');
      const bad = /page not found|we couldn't find this page|an error occurred while processing/i.test(t);
      if (bad) { failed++; check(`[${page.role}] ${path}: "${name}" leads to a working page`, false, after.url.replace(BASE, '')); }
      continue;
    }
    if (after.modal && !before.modal) {
      if (!(await closeModal(page))) { failed++; check(`[${page.role}] ${path}: dialog of "${name}" can be cancelled`, false); }
      continue;
    }
    if (same(before, after)) noEffect.push(name);
    else if (DANGER.test(`${next.text} ${next.label}`) || next.danger) notes.push(`info  [${page.role}] ${path} "${name}" acted without a confirmation dialog`);
  }
  await sleep(800); await brokenBy(last);
  for (const n of noEffect) notes.push(`quiet [${page.role}] ${path} "${n}" changed nothing visible`);
  check(`[${page.role}] crawl ${path}`, failed === 0, `${clicks} clicks, ${noEffect.length} without visible effect`);
}

// ---------------------------------------------------------------------------------------------------------------
// A failing step is reported and the next step still runs.
async function step(name, fn) {
  try { await fn(); } catch (e) { check(`${name}: step stopped`, false, e.message.split('\n')[0]); }
}

(async () => {
  const browser = await puppeteer.launch({
    executablePath: CHROME, headless: true,
    args: ['--no-sandbox', '--disable-renderer-backgrounding', '--disable-background-timer-throttling', '--disable-backgrounding-occluded-windows'],
  });
  const suffix = Date.now().toString(36);
  let sup, guest, reportId; // shared by the scenarios and the crawler
  try {
    const admin = await newPage(browser, 'admin');
    await login(admin, ADMIN);
    await go(admin, '/admin/surveys');
    const surveys = await admin.$$eval('a[href$="/edit"][href*="admin/surveys/"]', as => as.map(a => ({
      id: a.getAttribute('href').split('/')[2], title: a.closest('tr')?.querySelector('a:not(.btn)')?.innerText.trim() || '',
    })));
    const csat = surveys.find(s => s.title.startsWith('Customer Satisfaction')) || surveys[0];
    check('survey list shows the demo surveys', surveys.length >= 3, `${surveys.length} surveys`);

    // ----- Preview from the survey list (actions menu) ------------------------------------------------------
    await step('Preview from the survey list (actions menu)', async () => {
      await clickText(admin, 'button[data-bs-toggle=dropdown]', `More actions for ${csat.title}`); await sleep(400);
      await clickText(admin, 'a.dropdown-item', 'Preview');
      await waitFor(() => admin.url().endsWith(`/admin/surveys/${csat.id}/preview`)); await sleep(1500);
      check('survey list → Preview opens the preview page', admin.url().endsWith(`/admin/surveys/${csat.id}/preview`), admin.url());
      check('preview shows the survey title and its questions',
        (await text(admin)).includes(`Preview “${csat.title}”`) && (await count(admin, '.question-card')) > 0);
    });

    // ----- Preview page: width toggle, runner buttons, restart, header buttons ------------------------------
    await step('Preview page', async () => {
      await untilReacts(admin, () => clickText(admin, 'button', 'Phone'), async () => !!(await admin.$('.preview-stage.preview-phone')), 'Phone toggle');
      check('preview: Phone button narrows the preview', !!(await admin.$('.preview-stage.preview-phone')));
      await clickText(admin, 'button', 'Desktop'); await sleep(500);
      check('preview: Desktop button widens it again', !(await admin.$('.preview-stage.preview-phone')));
      const first = await pageIndicator(admin);
      await answerPage(admin);
      await clickText(admin, 'button', 'Next');
      await waitFor(async () => (await pageIndicator(admin)) !== first, 5000);
      const second = await pageIndicator(admin);
      check('preview: Next goes to the next page', second !== first && /Page 2/.test(second), `${first} → ${second}`);
      await clickText(admin, 'button', 'Back');
      await waitFor(async () => (await pageIndicator(admin)) === first, 5000);
      check('preview: Back returns to the previous page', (await pageIndicator(admin)) === first);
      await walkToSubmit(admin);
      await clickText(admin, 'button', 'Submit');
      await waitFor(async () => (await text(admin)).includes('Restart preview'));
      check('preview: Submit shows the preview thank-you page (nothing stored)', (await text(admin)).includes('Restart preview'));
      await clickText(admin, 'button', 'Restart preview');
      await waitFor(async () => (await count(admin, '.question-card')) > 0);
      check('preview: Restart preview starts over', (await pageIndicator(admin)) === first && (await count(admin, '.question-card')) > 0);
      await clickText(admin, 'a.btn', 'Share'); await waitFor(() => admin.url().endsWith('/share')); await sleep(1200);
      check('preview: Share button opens the share page', admin.url().endsWith(`/admin/surveys/${csat.id}/share`), admin.url());
    });

    // ----- Share page: Preview link, copy buttons, QR downloads --------------------------------------------
    await step('Share page', async () => {
      await clickText(admin, 'a.btn', 'Preview'); await waitFor(() => admin.url().endsWith('/preview')); await sleep(1200);
      check('share page → Preview opens the preview', admin.url().endsWith(`/admin/surveys/${csat.id}/preview`), admin.url());
      await clickText(admin, 'a.btn', 'Edit'); await waitFor(() => admin.url().endsWith('/edit')); await sleep(1500);
      check('preview: Edit button opens the builder', admin.url().endsWith(`/admin/surveys/${csat.id}/edit`), admin.url());
      await go(admin, `/admin/surveys/${csat.id}/share`);
      for (const [button, toast] of [['Copy link', 'Link copied'], ['Copy text', 'Invitation copied'], ['Copy code', 'Embed code copied']]) {
        const before = (await gui(admin)).copies.length;
        await untilReacts(admin, () => clickText(admin, 'button', button), async () => (await gui(admin)).copies.length > before, button);
        const g = await gui(admin);
        check(`share: ${button} copies to the clipboard and confirms`, g.copies.length > before && (await waitFor(async () => (await toastText(admin)).includes(toast), 3000)),
          g.copies[g.copies.length - 1].slice(0, 60).replace(/\s+/g, ' '));
      }
      for (const format of ['PNG', 'SVG']) {
        const before = (await gui(admin)).downloads.length;
        await clickText(admin, 'button', format);
        await waitFor(async () => (await gui(admin)).downloads.length > before, 5000);
        const d = (await gui(admin)).downloads.at(-1);
        check(`share: QR ${format} downloads a file`, (await gui(admin)).downloads.length > before && d.size > 100, d ? `${d.name} ${d.size} bytes` : 'no download');
      }
    });

    // ----- Builder: Preview (saved / unsaved), tabs, editing buttons, save, status menu --------------------
    await step('Builder', async () => {
      await go(admin, `/admin/surveys/${csat.id}/edit`);
      await untilReacts(admin, () => clickText(admin, 'button', 'Preview'), async () => admin.url().endsWith('/preview'), 'builder Preview');
      await sleep(1200);
      check('builder: Preview opens the preview of a saved survey', admin.url().endsWith(`/admin/surveys/${csat.id}/preview`), admin.url());
      await go(admin, `/admin/surveys/${csat.id}/edit`);
      await untilReacts(admin, () => clickText(admin, 'button.nav-link', 'Settings'), async () => !!(await admin.$('#s-title')), 'Settings tab');
      check('builder: Settings tab shows the survey settings', !!(await admin.$('#s-title')));
      const newTitle = `${csat.title} (GUI ${suffix})`;
      await admin.$eval('#s-title', e => { e.value = ''; });
      await admin.type('#s-title', newTitle);
      await admin.$eval('#s-title', e => e.dispatchEvent(new Event('change', { bubbles: true })));
      await waitFor(async () => (await admin.$$eval('button', bs => bs.some(b => b.innerText.includes('Save changes')))), 4000);
      check('builder: an edit enables "Save changes"', await admin.$$eval('button', bs => bs.some(b => b.innerText.includes('Save changes'))));
      await clickText(admin, 'button', 'Preview');
      await waitFor(() => admin.url().endsWith('/preview'), 8000); await sleep(1500);
      check('builder: Preview with unsaved changes saves first, then opens the preview',
        admin.url().endsWith(`/admin/surveys/${csat.id}/preview`) && (await text(admin)).includes(newTitle), admin.url());
      await go(admin, `/admin/surveys/${csat.id}/edit`);
      check('builder: the change was saved', (await text(admin)).includes(newTitle) && await admin.$$eval('button', bs => bs.some(b => b.innerText.trim() === 'Saved')));

      const questions = () => count(admin, '.question-editor');
      const q0 = await questions();
      await untilReacts(admin, () => clickText(admin, 'button[data-bs-toggle=dropdown]', 'Add question'),
        async () => !!(await admin.$('.type-picker button')) && !!(await admin.$('.dropdown-menu.show .type-picker')), 'Add question menu');
      await clickText(admin, '.type-picker button', 'Short text');
      await waitFor(async () => (await questions()) === q0 + 1, 4000);
      check('builder: Add question → Short text adds a question', (await questions()) === q0 + 1, `${q0} → ${await questions()}`);
      await fillQuestionText(admin, 'Anything else to tell us? (GUI check)');
      await clickText(admin, '.question-editor.active button', 'Duplicate question');
      await waitFor(async () => (await questions()) === q0 + 2, 4000);
      check('builder: Duplicate question copies it', (await questions()) === q0 + 2);
      const orderBefore = await admin.$$eval('.question-editor', es => es.map(e => e.id).join());
      await clickText(admin, '.question-editor.active button', 'Move question up');
      await sleep(700);
      check('builder: Move up reorders the questions', (await admin.$$eval('.question-editor', es => es.map(e => e.id).join())) !== orderBefore);
      await clickText(admin, '.question-editor.active button', 'Delete question'); await sleep(600);
      if (await admin.$('.ss-modal')) { await clickText(admin, '.ss-modal button', 'Delete'); await sleep(600); }
      check('builder: Delete question removes it', (await questions()) === q0 + 1);
      const sections = () => admin.$$eval('button[aria-label="Delete page"]', bs => bs.length);
      const s0 = await sections();
      await clickText(admin, 'button', 'Add page'); await sleep(700);
      check('builder: Add page adds a page', (await sections()) === s0 + 1, `${s0} → ${await sections()}`);
      const newest = (await admin.$$('.question-editor')).at(-1); // the new page starts with an empty question
      if (!(await newest.evaluate(e => e.classList.contains('active')))) { await (await newest.$('.qe-header')).click(); await sleep(600); }
      await fillQuestionText(admin, 'A question on the new page (GUI check)');
      await clickText(admin, 'button', 'Save changes');
      await waitFor(async () => (await admin.$$eval('button', bs => bs.some(b => b.innerText.trim() === 'Saved'))), 8000);
      check('builder: Save changes saves (button shows "Saved")', await admin.$$eval('button', bs => bs.some(b => b.innerText.trim() === 'Saved')), await toastText(admin));
      await clickText(admin, 'button[data-bs-toggle=dropdown]', 'Status'); await sleep(400);
      const statusItems = await admin.$$eval('.dropdown-menu.show .dropdown-item', es => es.map(e => e.innerText.trim()));
      check('builder: Status button lists the status actions', statusItems.length > 0, statusItems.join(', '));
      await admin.keyboard.press('Escape');
      await clickText(admin, 'button', 'Share'); await waitFor(() => admin.url().endsWith('/share')); await sleep(800);
      check('builder: Share button opens the share page', admin.url().endsWith(`/admin/surveys/${csat.id}/share`));
    });

    // ----- Survey list: new survey, duplicate, export definition, delete with confirmation ------------------
    await step('Survey list', async () => {
      await go(admin, '/admin/surveys/new');
      await fillQuestionText(admin, 'How did the GUI check go?');
      await untilReacts(admin, () => clickText(admin, 'button.nav-link', 'Settings'), async () => !!(await admin.$('#s-title')), 'Settings tab (new)');
      await admin.$eval('#s-title', e => { e.value = ''; });
      await admin.type('#s-title', `GUI survey ${suffix}`);
      await admin.$eval('#s-title', e => e.dispatchEvent(new Event('change', { bubbles: true })));
      await sleep(400);
      await clickText(admin, 'button', 'Create survey');
      await waitFor(() => /\/admin\/surveys\/[0-9a-f-]{36}\/edit$/.test(admin.url()), 8000);
      check('new survey: Create survey saves it and opens its builder', /\/admin\/surveys\/[0-9a-f-]{36}\/edit$/.test(admin.url()), admin.url());
      await go(admin, '/admin/surveys');
      const rows = () => count(admin, 'tbody tr');
      const r0 = await rows();
      await clickText(admin, 'button[data-bs-toggle=dropdown]', `More actions for GUI survey ${suffix}`); await sleep(400);
      await clickText(admin, '.dropdown-menu.show button', 'Duplicate');
      await waitFor(() => /\/admin\/surveys\/[0-9a-f-]{36}\/edit$/.test(admin.url()), 8000); await sleep(1200);
      check('survey list: Duplicate creates a copy and opens it in the builder',
        /\/admin\/surveys\/[0-9a-f-]{36}\/edit$/.test(admin.url()) && (await text(admin)).includes(`GUI survey ${suffix}`), await toastText(admin));
      await go(admin, '/admin/surveys');
      check('survey list: the copy is listed', (await rows()) === r0 + 1, `${r0} → ${await rows()} rows`);
      const dl0 = (await gui(admin)).downloads.length;
      await clickText(admin, 'button[data-bs-toggle=dropdown]', `More actions for GUI survey ${suffix}`); await sleep(400);
      await clickText(admin, '.dropdown-menu.show button', 'Export definition');
      await waitFor(async () => (await gui(admin)).downloads.length > dl0, 5000);
      const def = (await gui(admin)).downloads.at(-1);
      check('survey list: Export definition downloads JSON', (await gui(admin)).downloads.length > dl0 && def.name.endsWith('.json') && def.size > 50, def ? `${def.name} ${def.size} bytes` : 'none');
      const r1 = await rows();
      await clickText(admin, 'button[data-bs-toggle=dropdown]', `More actions for GUI survey ${suffix}`); await sleep(400);
      await clickText(admin, '.dropdown-menu.show button', 'Delete'); await sleep(700);
      check('survey list: Delete asks for confirmation', !!(await admin.$('.ss-modal')));
      await closeModal(admin);
      check('survey list: Cancel keeps the survey', (await rows()) === r1);
      await clickText(admin, 'button[data-bs-toggle=dropdown]', `More actions for GUI survey ${suffix}`); await sleep(400);
      await clickText(admin, '.dropdown-menu.show button', 'Delete'); await sleep(700);
      await clickText(admin, '.ss-modal button', 'Delete');
      await waitFor(async () => (await rows()) === r1 - 1, 6000);
      check('survey list: confirming Delete removes the survey', (await rows()) === r1 - 1, await toastText(admin));
    });

    // ----- Responses: list, detail, print, exports -----------------------------------------------------------
    await step('Responses', async () => {
      await go(admin, `/admin/surveys/${csat.id}/responses`);
      for (const format of ['Excel workbook', 'CSV', 'JSON']) {
        const before = (await gui(admin)).downloads.length;
        if (!(await admin.$('.dropdown-menu.show'))) { await clickText(admin, 'button[data-bs-toggle=dropdown]', 'Export'); await sleep(400); }
        await clickText(admin, '.dropdown-menu.show button', format);
        await waitFor(async () => (await gui(admin)).downloads.length > before, 8000);
        const d = (await gui(admin)).downloads.at(-1);
        check(`responses: Export → ${format} downloads a file`, (await gui(admin)).downloads.length > before && d.size > 100, d ? `${d.name} ${d.size} bytes` : 'none');
      }
      await admin.keyboard.press('Escape');
      await clickText(admin, 'a.btn', 'View'); await waitFor(() => admin.url().includes('/admin/responses/')); await sleep(1300);
      check('responses: View opens the response', admin.url().includes('/admin/responses/') && (await count(admin, '.card')) > 0, admin.url());
      const p0 = (await gui(admin)).prints;
      await untilReacts(admin, () => clickText(admin, 'button', 'Print'), async () => (await gui(admin)).prints > p0, 'Print');
      check('response: Print opens the print dialog', (await gui(admin)).prints > p0);
    });

    // ----- Reports: viewer exports, print, refresh; builder live preview and Add widget -----------------------
    await step('Reports', async () => {
      await go(admin, '/admin/reports');
      reportId = await admin.$eval('a.btn[title=View][href^="admin/reports/"]', a => a.getAttribute('href').split('/')[2]);
      await go(admin, `/admin/reports/${reportId}`);
      check('report viewer shows widgets', (await count(admin, '.widget-card')) > 0);
      const exportFormats = await (async () => {
        await clickText(admin, 'button[data-bs-toggle=dropdown]', 'Export'); await sleep(400);
        const items = await admin.$$eval('.dropdown-menu.show .dropdown-item', es => es.map(e => e.innerText.trim()));
        await admin.keyboard.press('Escape'); await sleep(300);
        return items;
      })();
      for (const format of exportFormats) {
        const before = (await gui(admin)).downloads.length;
        if (!(await admin.$('.dropdown-menu.show'))) { await clickText(admin, 'button[data-bs-toggle=dropdown]', 'Export'); await sleep(400); }
        await clickText(admin, '.dropdown-menu.show .dropdown-item', format);
        await waitFor(async () => (await gui(admin)).downloads.length > before, 15000);
        const d = (await gui(admin)).downloads.at(-1);
        check(`report: Export → ${format} downloads a file`, (await gui(admin)).downloads.length > before && d.size > 100, d ? `${d.name} ${d.size} bytes` : 'none');
      }
      const p1 = (await gui(admin)).prints;
      await clickText(admin, 'button', 'Print'); await waitFor(async () => (await gui(admin)).prints > p1, 3000);
      check('report: Print opens the print dialog', (await gui(admin)).prints > p1);
      await go(admin, `/admin/reports/new?surveyId=${csat.id}`);
      const previewHeader = () => admin.$eval('.report-preview', e => e.innerText).catch(() => '');
      await waitFor(async () => /\d+ responses?/.test(await previewHeader()), 10000);
      const w0 = await count(admin, '.report-preview .widget-card');
      check('report builder: live preview runs on the survey\'s responses', /\d+ responses?/.test(await previewHeader()),
        ((await previewHeader()).match(/\d+ responses?/) || ['no count'])[0]);
      await clickText(admin, 'button[data-bs-toggle=dropdown]', 'Add widget'); await sleep(400);
      await admin.$$eval('.dropdown-menu.show .dropdown-item:not([disabled])', es => es[0].click());
      await waitFor(async () => (await count(admin, '.report-preview .widget-card')) > w0, 8000);
      check('report builder: Add widget updates the live preview', (await count(admin, '.report-preview .widget-card')) > w0, `${w0} → ${await count(admin, '.report-preview .widget-card')}`);
    });

    // ----- Users dialog, theme toggle, workspace settings -----------------------------------------------------
    await step('Users dialog, theme toggle, workspace settings', async () => {
      await go(admin, '/admin/users');
      await untilReacts(admin, () => clickText(admin, 'button', 'New user'), async () => !!(await admin.$('.ss-modal #nu-email')), 'New user');
      check('users: New user opens the dialog', !!(await admin.$('.ss-modal #nu-email')));
      check('users: Cancel closes the dialog', await closeModal(admin));
      const theme = () => admin.evaluate(() => document.documentElement.getAttribute('data-bs-theme'));
      const t0 = await theme();
      await admin.click('button.theme-toggle'); await sleep(400);
      const t1 = await theme();
      await admin.click('button.theme-toggle'); await sleep(400);
      check('theme toggle switches light/dark and back', t0 !== t1 && (await theme()) === t0, `${t0} → ${t1} → ${await theme()}`);
      await admin.click('.app-topbar button[data-bs-toggle=dropdown], header button[data-bs-toggle=dropdown]'); await sleep(400);
      const menu = await admin.$$eval('.dropdown-menu.show', ms => ms.map(m => m.innerText).join(' '));
      check('user menu opens with the account links and Log out', menu.includes('Log out'), menu.replace(/\s+/g, ' ').slice(0, 80));
      await admin.keyboard.press('Escape');
    });

    // ----- Super admin: branding live preview, workspace dialogs, account dialog ------------------------------
    await step('Super admin', async () => {
      sup = await newPage(browser, 'superadmin');
      await login(sup, SUPER);
      await go(sup, '/system/branding');
      const brand = () => sup.$eval('.brand-preview-bar .brand-name', e => e.innerText.trim());
      await untilReacts(sup, async () => {
        await sup.$eval('#b-name', e => { e.value = ''; }); await sup.type('#b-name', `Pulse ${suffix}`);
        await sup.$eval('#b-name', e => e.dispatchEvent(new Event('change', { bubbles: true })));
      },
        async () => (await brand()) === `Pulse ${suffix}`, 'branding name');
      check('branding: the live preview follows the product name', (await brand()) === `Pulse ${suffix}`);
      await go(sup, '/system/workspaces');
      await clickText(sup, 'a', 'Default workspace'); await sup.waitForNetworkIdle(); await sleep(1300);
      await clickText(sup, 'button', 'Disable workspace'); await sleep(600);
      check('workspace: Disable workspace… asks for a reason first', !!(await sup.$('#wd-reason')));
      check('workspace: Cancel closes the dialog (workspace stays enabled)', (await closeModal(sup)) && !(await text(sup)).includes('Enable workspace'));
      await go(sup, '/system/accounts');
      await untilReacts(sup, () => clickText(sup, 'button', 'New account'), async () => !!(await sup.$('.ss-modal #nu-email')), 'New account');
      check('accounts: New account opens the dialog', !!(await sup.$('.ss-modal #nu-email')));
      await closeModal(sup);
    });

    // ----- Guest: public runner to the thank-you page --------------------------------------------------------
    await step('Guest', async () => {
      guest = await newPage(browser, 'guest');
      await go(guest, '/w/default');
      await guest.click('a[href="s/customer-satisfaction-survey"]'); await guest.waitForNetworkIdle(); await sleep(1500);
      check('guest: workspace page → survey opens the runner', (await count(guest, '.question-card')) > 0, guest.url());
      const walked = await walkToSubmit(guest);
      await clickText(guest, 'button', 'Submit');
      await waitFor(() => guest.url().includes('/thank-you'), 15000); await sleep(800);
      check('guest: Next… Submit stores the response and shows the thank-you page', guest.url().includes('/thank-you'), `${walked} pages, ${guest.url().replace(BASE, '')}`);
    });

    // ----- Crawler ---------------------------------------------------------------------------------------------
    await step('Crawler', async () => {
      await go(admin, '/admin/surveys');
      const builderId = await admin.$eval('a[href$="/edit"][href*="admin/surveys/"]', a => a.getAttribute('href').split('/')[2]);
      await go(admin, `/admin/surveys/${csat.id}/responses`);
      const responseId = await admin.$eval('a[href^="admin/responses/"]', a => a.getAttribute('href').split('/')[2]);
      await go(sup, '/system/workspaces');
      const workspaceId = await sup.$$eval('a[href^="system/workspaces/"]', as => as.map(a => a.getAttribute('href').split('/')[2])
        .find(id => /^[0-9a-f-]{36}$/.test(id)));
      await go(guest, '/w/default');
      const slug = await guest.$eval('a[href^="s/"]', a => a.getAttribute('href').slice(2));

      // Every role crawls in a fresh signed-in page, closed afterwards: Chrome may discard pages that sat idle for minutes.
      for (const p of [admin, sup, guest]) await p.browserContext().close();
      const plan = [
        ['guest', null, ['/', '/surveys', '/w/default', `/s/${slug}`, '/faq', '/guide', '/signup', '/Account/Login', '/Account/Register',
          '/Account/ForgotPassword', '/buy-me-a-coffee']],
        ['member', MEMBER, ['/surveys', '/my/responses', '/Account/Manage', '/Account/Manage/Email', '/Account/Manage/ChangePassword',
          '/Account/Manage/TwoFactorAuthentication', '/Account/Manage/PersonalData']],
        ['admin', ADMIN, ['/admin', '/admin/surveys', `/admin/surveys/${csat.id}/preview`, `/admin/surveys/${csat.id}/share`,
          `/admin/surveys/${csat.id}/responses`, `/admin/responses/${responseId}`, '/admin/reports', `/admin/reports/${reportId}`,
          `/admin/reports/${reportId}/edit`, '/admin/reports/new', '/admin/users', '/admin/settings', '/admin/audit',
          '/admin/surveys/new', `/admin/surveys/${builderId}/edit`]],
        ['superadmin', SUPER, ['/system', '/system/workspaces', `/system/workspaces/${workspaceId}`, '/system/workspaces/new',
          '/system/accounts', '/system/branding', '/system/settings', '/system/audit']],
      ];
      for (const [role, credentials, paths] of plan) {
        const page = await newPage(browser, role);
        if (credentials) await login(page, credentials);
        const seenLinks = new Set();
        for (const path of paths) {
          try { await crawl(page, path, seenLinks); } catch (e) { check(`[${role}] crawl ${path}`, false, e.message.split('\n')[0]); }
        }
        await page.browserContext().close();
      }
    });
  } catch (e) { check('script', false, e.message.split('\n')[0]); }
  await browser.close();
  for (const r of results) console.log(r.join('  '));
  if (notes.length) console.log('---- notes (review, not failures) ----\n' + notes.join('\n'));
  const unique = [...new Set(errors)];
  console.log(unique.length ? '---- browser errors ----\n' + unique.join('\n') : 'no browser errors');
  const failed = results.filter(r => r[0].startsWith('FAIL')).length;
  console.log(`${results.length - failed}/${results.length} checks passed`);
  process.exit(failed || unique.length ? 1 : 0);
})();
