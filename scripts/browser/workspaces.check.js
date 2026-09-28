// Headless-browser check of the multi-workspace flows (real Blazor circuits, not just HTTP):
// sign-up → own workspace, settings, join link, super admin disables → member locked out, enable again,
// super admin creates a super admin, role boundaries, survey runner across workspaces. Collects console
// errors, failed requests and 5xx responses.
//
// Run against a FRESH instance with demo data (it creates workspaces and accounts), e.g. on SQLite:
//   cd src/SmartSurvey.Web && ASPNETCORE_ENVIRONMENT=Development Database__Provider=Sqlite \
//     ConnectionStrings__DefaultConnection="Data Source=/tmp/browser.db" Seed__DemoData=true \
//     Seed__AdminPassword='Admin123!' Seed__SuperAdminPassword='SuperAdmin123!' Https__Redirect=false \
//     dotnet run --urls http://localhost:5230
//   cd scripts/browser && npm install && npm run check        # BASE=… CHROME_PATH=… to override
// Exit code 0 when every check passed and the browser reported no errors.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'http://localhost:5230';
const CHROME = process.env.CHROME_PATH || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const results = []; const errors = [];
const check = (name, ok, info = '') => results.push([ok ? 'OK  ' : 'FAIL', name, info]);
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function newPage(browser) {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  page.setDefaultTimeout(15000);
  page.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
  page.on('requestfailed', r => { if (!r.url().includes('_blazor') && !r.url().includes('/_framework/')) errors.push(`requestfailed: ${r.url()} ${r.failure()?.errorText}`); });
  page.on('response', r => { if (r.status() >= 500) errors.push(`HTTP ${r.status()} ${r.url()}`); });
  return page;
}
const go = async (page, path) => { await page.goto(BASE + path, { waitUntil: 'networkidle0' }); await sleep(1300); };
const text = page => page.evaluate(() => document.body.innerText);
async function login(page, email, password) {
  await go(page, '/Account/Login');
  await page.type('#login-email', email); await page.type('#login-password', password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
}
// Interactive pages ignore input until the Blazor circuit is connected: retype until the page reacts.
async function typeUntil(page, selector, value, reacted) {
  for (let attempt = 0; attempt < 6; attempt++) {
    await page.$eval(selector, e => { e.value = ''; });
    await page.type(selector, value);
    await sleep(500);
    if (await reacted()) return;
    await sleep(1000);
  }
  throw new Error(`page did not react to typing into ${selector} on ${page.url()}`);
}
const hasButton = (page, t) => page.$$eval('button', (bs, t) => bs.some(b => b.textContent.includes(t)), t);

async function clickText(page, selector, t) {
  for (const h of await page.$$(selector)) {
    if ((await h.evaluate(e => e.textContent)).includes(t)) { await h.click(); return; }
  }
  throw new Error(`no ${selector} containing "${t}" on ${page.url()}`);
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: CHROME, headless: true, args: ['--no-sandbox'] });
  const suffix = Date.now().toString(36);
  const email = `founder-${suffix}@example.com`, pw = 'Founder123!', wsName = `Browser Team ${suffix}`;
  try {
    const founder = await newPage(browser);
    await go(founder, '/signup');
    await founder.type('#signup-workspace', wsName);
    await founder.type('#signup-name', 'Browser Founder');
    await founder.type('#signup-email', email);
    await founder.type('#signup-password', pw);
    await founder.type('#signup-confirm', pw);
    await Promise.all([founder.waitForNavigation({ waitUntil: 'networkidle0' }), founder.click('button[type=submit]')]);
    await sleep(1300);
    check('sign-up lands on /admin', founder.url().endsWith('/admin'), founder.url());
    check('sidebar shows the new workspace', (await text(founder)).includes(wsName));

    await go(founder, '/admin/surveys');
    check('new workspace sees none of the demo surveys', !(await text(founder)).includes('Customer Satisfaction Survey'));

    await go(founder, '/admin/settings');
    await typeUntil(founder, '#ws-contact', 'team@example.com', () => hasButton(founder, 'Save changes'));
    await clickText(founder, 'button', 'Save changes'); await sleep(1500);
    check('workspace settings saved (toast)', (await text(founder)).includes('Settings saved'));
    const slug = await founder.$eval('dd.font-monospace', e => e.textContent.replace('/w/', '').trim());

    await go(founder, '/admin/users');
    check('users page shows the join link', !!(await founder.$("input[aria-label='Join link']")));

    const sup = await newPage(browser);
    await login(sup, 'superadmin@smartsurvey.local', 'SuperAdmin123!');
    await go(sup, '/system/workspaces');
    check('System console lists the new workspace', (await text(sup)).includes(wsName));
    await clickText(sup, 'a', wsName); await sup.waitForNetworkIdle(); await sleep(1500);
    await clickText(sup, 'button', 'Disable workspace'); await sleep(600);
    await sup.type('#wd-reason', 'Browser check');
    await clickText(sup, '.ss-modal button', 'Disable'); await sleep(1500);
    check('workspace shows Disabled with the reason', (await text(sup)).includes('Browser check'));

    await founder.goto(BASE + '/admin', { waitUntil: 'networkidle0' }); await sleep(800);
    check('disabled: member is sent to /workspace-unavailable', founder.url().includes('workspace-unavailable'), founder.url());
    await go(founder, `/w/${slug}`);
    check('disabled: workspace page is not found', (await text(founder)).includes('Workspace not found'));
    await login(founder, email, pw);
    check('disabled: sign-in explains why', founder.url().includes('workspace-unavailable'), founder.url());

    await clickText(sup, 'button', 'Enable workspace'); await sleep(1500);
    await login(founder, email, pw);
    await go(founder, '/admin');
    check('enabled again: member signs in', founder.url().endsWith('/admin') && (await text(founder)).includes(wsName), founder.url());

    await go(sup, '/system/accounts');
    await clickText(sup, 'button', 'New account'); await sleep(600);
    await sup.select('#nu-kind', 'superadmin'); await sleep(500);
    check('account dialog hides the workspace for super admins', !(await sup.$('#nu-workspace')));
    await sup.type('#nu-email', `ops-${suffix}@example.com`);
    await sup.type('#nu-password', 'Operator123!');
    await clickText(sup, '.ss-modal button', 'Create account'); await sleep(1500);
    check('super admin account created in the System console', (await text(sup)).includes(`ops-${suffix}@example.com`));

    await founder.goto(BASE + '/system', { waitUntil: 'networkidle0' });
    check('workspace admin cannot open the System console', founder.url().includes('AccessDenied'), founder.url());

    const guest = await newPage(browser);
    await go(guest, '/s/customer-satisfaction-survey');
    check('public survey runner renders questions', (await guest.$$('.question-card')).length > 0);

    const member = await newPage(browser);
    await login(member, 'user@smartsurvey.local', 'User123!');
    await go(member, '/s/acme-team-offsite-feedback');
    const mt = await text(member);
    check('member of another workspace gets the survey (as a guest) or a clear message',
      (await member.$$('.question-card')).length > 0 || mt.includes('another workspace'), mt.slice(0, 100).replace(/\s+/g, ' '));
    await go(member, '/surveys');
    check('member /surveys shows the own workspace', (await text(member)).includes('Surveys of Default workspace'));
  } catch (e) { check('script', false, e.message); }
  await browser.close();
  for (const r of results) console.log(r.join('  '));
  const unique = [...new Set(errors)];
  console.log(unique.length ? '---- browser errors ----\n' + unique.join('\n') : 'no browser errors');
  process.exit(results.some(r => r[0].startsWith('FAIL')) || unique.length ? 1 : 0);
})();
