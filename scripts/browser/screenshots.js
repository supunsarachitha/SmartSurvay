// Reproducible capture of the user-guide / README screenshots (src/SmartSurvey.Web/wwwroot/img/guide).
//
// Run against a FRESH instance with demo data (it adds a pending workspace and saves workspace settings), e.g.:
//   cd src/SmartSurvey.Web && ASPNETCORE_ENVIRONMENT=Development Database__Provider=Sqlite \
//     ConnectionStrings__DefaultConnection="Data Source=/tmp/shots.db" Seed__DemoData=true \
//     Seed__AdminPassword='Admin123!' Seed__SuperAdminPassword='SuperAdmin123!' Https__Redirect=false \
//     App__PublicBaseUrl=https://surveys.example.com dotnet run --urls http://localhost:5230
//   cd scripts/browser && npm install && npm run screenshots      # OUT=… to write elsewhere (review first!)
// Desktop images are 1280×800, the phone image 390×844 at 2× (780×1688), all WebP.
const puppeteer = require('puppeteer-core');
const path = require('path');
const BASE = process.env.BASE || 'http://localhost:5230';
const OUT = process.env.OUT || path.join(__dirname, '../../src/SmartSurvey.Web/wwwroot/img/guide');
const CHROME = process.env.CHROME_PATH || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const SURVEY_SLUG = 'customer-satisfaction-survey';
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function api(route, method = 'GET', body, token) {
  const r = await fetch(BASE + route, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!r.ok) throw new Error(`${method} ${route}: ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}
const token = async (email, password) => (await api('/api/auth/login', 'POST', { email, password })).accessToken;

async function newPage(browser, viewport = { width: 1280, height: 800, deviceScaleFactor: 1 }) {
  const context = await browser.createBrowserContext();
  const page = await context.newPage();
  page.setDefaultTimeout(20000);
  await page.setViewport(viewport);
  return page;
}
const go = async (page, route) => { await page.goto(BASE + route, { waitUntil: 'networkidle0' }); await sleep(1500); };
async function login(page, email, password) {
  await go(page, '/Account/Login');
  await page.type('#login-email', email);
  await page.type('#login-password', password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
}
async function shot(page, name) {
  await sleep(700);
  await page.screenshot({ path: path.join(OUT, `${name}.webp`), type: 'webp', quality: 82 });
  console.log('saved', name);
}
// Interactive (Blazor Server) pages ignore input until their circuit is connected: repeat until the page reacts.
async function until(action, reacted, what) {
  for (let attempt = 0; attempt < 8; attempt++) {
    await action();
    await sleep(600);
    if (await reacted()) return;
    await sleep(900);
  }
  throw new Error(`no reaction: ${what}`);
}
const clickText = (page, selector, text) => page.$$eval(selector, (els, text) => {
  const el = els.find(e => e.textContent.includes(text));
  if (el) el.click();
  return !!el;
}, text);
const exists = (page, selector) => page.$(selector).then(Boolean);

(async () => {
  // Data the pictures need: a workspace waiting for approval (System overview).
  const superToken = await token('superadmin@smartsurvey.local', 'SuperAdmin123!');
  await api('/api/v1/system/settings', 'PUT', { allowWorkspaceSignup: true, requireWorkspaceApproval: true, provideStarterTemplates: true, supportEmail: 'support@example.com' }, superToken);
  await api('/api/v1/public/workspaces', 'POST', { workspaceName: 'City Book Club', displayName: 'Robin', email: 'robin@example.com', password: 'BookClub123!' })
    .catch(e => { if (!String(e.message).includes(': 409')) throw e; }); // already there on a second run
  await api('/api/v1/system/settings', 'PUT', { allowWorkspaceSignup: true, requireWorkspaceApproval: false, provideStarterTemplates: true, supportEmail: 'support@example.com' }, superToken);
  const adminToken = await token('admin@smartsurvey.local', 'Admin123!');
  const survey = (await api(`/api/v1/surveys?search=${SURVEY_SLUG}`, 'GET', null, adminToken)).items.find(s => s.slug === SURVEY_SLUG);
  const report = (await api(`/api/v1/reports?surveyId=${survey.id}`, 'GET', null, adminToken)).items[0];

  const browser = await puppeteer.launch({ executablePath: CHROME, headless: true, args: ['--no-sandbox', '--hide-scrollbars'] });
  try {
    // ----- public pages
    const guest = await newPage(browser);
    await go(guest, '/'); await shot(guest, 'home');
    await go(guest, '/guide'); await shot(guest, 'guide');
    await go(guest, `/s/${SURVEY_SLUG}`);
    await until(() => clickText(guest, '.choice-item', 'Manager'), () => exists(guest, '.choice-item.selected'), 'choose an answer');
    await guest.evaluate(() => window.scrollTo(0, 0));
    await shot(guest, 'survey');

    const phone = await newPage(browser, { width: 390, height: 844, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    await go(phone, `/s/${SURVEY_SLUG}`);
    await until(() => clickText(phone, '.choice-item', 'Manager'), () => exists(phone, '.choice-item.selected'), 'choose an answer (phone)');
    await phone.evaluate(() => {
      // Progress bar, page title and the first question with its chosen answer.
      const question = document.querySelector('.question-card');
      window.scrollTo(0, question.getBoundingClientRect().top + window.scrollY - 300);
    });
    await shot(phone, 'survey-mobile');

    // ----- workspace admin
    const admin = await newPage(browser);
    await login(admin, 'admin@smartsurvey.local', 'Admin123!');
    await go(admin, '/admin/settings');
    await until(async () => { await admin.$eval('#ws-description', e => { e.value = ''; }); await admin.type('#ws-description', 'Customer and employee surveys of the demo company.'); },
      () => admin.$$eval('button', bs => bs.some(b => b.textContent.includes('Save changes'))), 'edit workspace settings');
    await admin.type('#ws-contact', 'surveys@example.com');
    await clickText(admin, 'button', 'Save changes');
    await sleep(6000); // let the toast fade
    await go(admin, '/admin/settings'); await shot(admin, 'workspace-settings');
    await go(admin, '/admin'); await shot(admin, 'dashboard');
    await admin.evaluate(() => localStorage.setItem('ss-theme', 'dark'));
    await go(admin, '/admin'); await shot(admin, 'dashboard-dark');
    await admin.evaluate(() => localStorage.setItem('ss-theme', 'light'));
    await go(admin, '/admin/surveys'); await shot(admin, 'survey-list');

    await go(admin, `/admin/surveys/${survey.id}/edit`); await shot(admin, 'builder');
    await until(() => clickText(admin, '.qe-header', 'What could we improve?'),
      () => admin.$$eval('.question-editor.active', els => els.some(e => e.textContent.includes('What could we improve?'))), 'open a question');
    await admin.evaluate(() => {
      const editor = [...document.querySelectorAll('.question-editor.active')].find(e => e.textContent.includes('What could we improve?'));
      const logic = [...editor.querySelectorAll('details')].find(d => d.textContent.includes('Display logic'));
      logic.open = true;
      logic.scrollIntoView({ block: 'center' });
    });
    await shot(admin, 'builder-logic');

    await go(admin, `/admin/surveys/${survey.id}/edit`);
    await until(() => clickText(admin, '.nav-link, button, a', 'Settings'), () => admin.$$eval('h2', hs => hs.some(h => h.textContent.includes('Basics'))), 'open the Settings tab');
    await admin.evaluate(() => window.scrollTo(0, 0));
    await shot(admin, 'survey-settings');

    await go(admin, `/admin/surveys/${survey.id}/preview`);
    await until(() => clickText(admin, 'button', 'Phone'), () => exists(admin, '.preview-phone'), 'phone preview');
    await shot(admin, 'preview');
    await go(admin, `/admin/surveys/${survey.id}/share`); await shot(admin, 'share');
    await go(admin, `/admin/surveys/${survey.id}/responses`); await shot(admin, 'responses');
    await go(admin, `/admin/reports/${report.id}`); await shot(admin, 'report');
    await go(admin, `/admin/reports/${report.id}/edit`); await sleep(1500); await shot(admin, 'report-builder');
    await go(admin, '/admin/users'); await shot(admin, 'users');

    // ----- member: account settings
    const member = await newPage(browser);
    await login(member, 'user@smartsurvey.local', 'User123!');
    await go(member, '/Account/Manage'); await shot(member, 'account-settings');

    // ----- super admin
    const sup = await newPage(browser);
    await login(sup, 'superadmin@smartsurvey.local', 'SuperAdmin123!');
    await go(sup, '/system'); await shot(sup, 'system-overview');
    await go(sup, '/system/branding'); await shot(sup, 'branding');
  } finally {
    await browser.close();
  }
})().catch(e => { console.error(e); process.exit(1); });
