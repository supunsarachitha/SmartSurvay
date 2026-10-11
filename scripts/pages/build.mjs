#!/usr/bin/env node
// Builds the project website of SmartSurvey (GitHub Pages) into the root of the repository.
//
//   cd scripts/pages && npm ci && npm run build
//
// GitHub Pages serves the root of main, so the generated pages are committed there; run this after every
// release and whenever one of the sources below changes, and commit the result:
//   * docs/DOCUMENTATION.md  -> docs.html        * CHANGELOG.md -> changelog.html
//   * the guide screenshots  -> img/             * Inter + its licence -> assets/fonts/
//   * the version (Directory.Build.props) and the release date (CHANGELOG.md) -> index.html
//   * the texts of the start page and of the 404 page are in this file
//
// assets/site.css, assets/site.js and assets/favicon.svg are written by hand and only referenced here.
// The two Mermaid diagrams of the documentation and the PNG icons are drawn with a local Chrome
// (CHROME_PATH); without one the diagrams stay code blocks and the existing icons are kept.
// The build ends with a link check and fails when a link or an anchor of the site points nowhere.
// More in README.md next to this file.

import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Marked, Renderer } from 'marked';
import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import csharp from 'highlight.js/lib/languages/csharp';
import http from 'highlight.js/lib/languages/http';
import json from 'highlight.js/lib/languages/json';
import powershell from 'highlight.js/lib/languages/powershell';
import sql from 'highlight.js/lib/languages/sql';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';

// Code blocks are coloured while building; the pages only carry the resulting <span> elements.
for (const [name, language] of Object.entries({ bash, csharp, http, json, powershell, sql, xml, yaml })) hljs.registerLanguage(name, language);
const CODE_LABELS = { bash: 'Shell', csharp: 'C#', http: 'HTTP', json: 'JSON', powershell: 'PowerShell', sql: 'SQL', xml: 'XML', yaml: 'YAML' };

const toolsDir = path.dirname(fileURLToPath(import.meta.url));
const repoDir = path.resolve(toolsDir, '../..');
const siteDir = repoDir;       // GitHub Pages serves the root of the repository

const REPO_URL = 'https://github.com/supunsarachitha/SmartSurvay';
const SITE_URL = 'https://supunsarachitha.github.io/SmartSurvay/';
const SITE_PATH = new URL(SITE_URL).pathname;
const COFFEE_URL = 'https://buymeacoffee.com/jkhy9gtjs';
const CHROME = process.env.CHROME_PATH || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';

const SHOTS_DIR = 'src/SmartSurvey.Web/wwwroot/img/guide';
const FONT_DIR = 'src/SmartSurvey.Web/wwwroot/lib/inter';
const FONT_FILE = 'inter-latin-wght-normal.woff2';

// ---------------------------------------------------------------------------------------------
// Sources
// ---------------------------------------------------------------------------------------------

const source = file => readFileSync(path.join(repoDir, file), 'utf8');
const sourceBytes = file => readFileSync(path.join(repoDir, file));

/** "blob", "tree" or null when the path does not exist in the repository. */
function sourceType(file) {
    try {
        return statSync(path.join(repoDir, file)).isDirectory() ? 'tree' : 'blob';
    } catch {
        return null;
    }
}

function write(file, content) {
    const target = path.join(siteDir, file);
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, content);
}

const esc = text => String(text).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const unesc = text => text.replace(/&(amp|lt|gt|quot|#39);/g, (_, e) => ({ amp: '&', lt: '<', gt: '>', quot: '"', '#39': "'" })[e]);

// ---------------------------------------------------------------------------------------------
// Markdown
// ---------------------------------------------------------------------------------------------

/** Heading ids the way GitHub builds them, so the anchors used in the Markdown files keep working. */
function createSlugger() {
    const seen = new Map();
    return text => {
        const base = text.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, '').replace(/ /g, '-');
        const count = seen.get(base) ?? 0;
        seen.set(base, count + 1);
        return count ? `${base}-${count}` : base;
    };
}

/** Where a relative link of a Markdown file leads on the site: a page of the site or the file on GitHub. */
function resolveLink(href, fromDir) {
    if (/^([a-z][a-z0-9+.-]*:|\/\/|#)/i.test(href)) return href;

    const [file, anchor] = href.split('#');
    const target = path.posix.normalize(path.posix.join(fromDir, file));
    const hash = anchor ? `#${anchor}` : '';
    if (target === 'docs/DOCUMENTATION.md') return `docs.html${hash}`;
    if (target === 'CHANGELOG.md') return `changelog.html${hash}`;

    const type = sourceType(target);
    if (!type) throw new Error(`Link "${href}" points to "${target}", which does not exist in the repository.`);
    return `${REPO_URL}/${type === 'tree' ? 'tree' : 'blob'}/main/${target}${hash}`;
}

/**
 * Renders a Markdown file of the repository. Returns the HTML, the headings (for the table of contents)
 * and the sources of the Mermaid diagrams, which are left as <!--diagram:n--> placeholders.
 */
function renderMarkdown(markdown, fromDir) {
    const slug = createSlugger();
    const headings = [];
    const diagrams = [];

    const marked = new Marked({
        gfm: true,
        walkTokens(token) {
            if (token.type === 'link') token.href = resolveLink(token.href, fromDir);
            if (token.type === 'image' && !/^[a-z][a-z0-9+.-]*:/i.test(token.href)) {
                const file = path.posix.normalize(path.posix.join(fromDir, token.href));
                write(`img/${path.posix.basename(file)}`, sourceBytes(file));
                token.href = `img/${path.posix.basename(file)}`;
            }
        },
        renderer: {
            heading({ tokens, depth }) {
                const html = this.parser.parseInline(tokens);
                const text = unesc(html.replace(/<[^>]+>/g, ''));
                const id = slug(text);
                headings.push({ depth, id, text });
                const anchor = depth > 1 ? `<a class="anchor" href="#${id}" aria-label="Link to this section">#</a>` : '';
                return `<h${depth} id="${id}">${html}${anchor}</h${depth}>\n`;
            },
            code({ text, lang }) {
                if (lang === 'mermaid') {
                    diagrams.push({ source: text, label: headings.at(-1)?.text ?? 'Diagram' });
                    return `<!--diagram:${diagrams.length - 1}-->\n`;
                }
                // A bar with the name of the language above blocks that have one; site.js adds the copy button.
                const language = (lang ?? '').trim().split(/\s+/)[0];
                const known = Boolean(language && hljs.getLanguage(language));
                const body = known ? hljs.highlight(text, { language }).value : esc(text);
                const label = CODE_LABELS[language] ?? '';
                return `<div class="code-wrap${label ? ' has-head' : ''}">${label ? `<div class="code-head"><span>${label}</span></div>` : ''}`
                    + `<pre><code${known ? ` class="language-${language}"` : ''}>${body}</code></pre></div>\n`;
            },
            // Wide tables scroll inside their own box.
            table(token) {
                return `<div class="table-wrap">${Renderer.prototype.table.call(this, token)}</div>\n`;
            },
        },
    });

    return { html: marked.parse(markdown), headings, diagrams };
}

/** Nested list of the headings between two levels. */
function tableOfContents(headings, from, to) {
    let html = '<ol>';
    let open = false;       // an <li> of the top level is open
    let nested = false;     // ... and it holds an open <ol>
    for (const { depth, id, text } of headings.filter(h => h.depth >= from && h.depth <= to)) {
        const link = `<a href="#${id}">${esc(text)}</a>`;
        if (depth === from) {
            if (nested) html += '</ol>';
            if (open) html += '</li>';
            html += `<li>${link}`;
            open = true;
            nested = false;
        } else {
            if (!nested) html += '<ol>';
            html += `<li>${link}</li>`;
            nested = true;
        }
    }
    if (nested) html += '</ol>';
    if (open) html += '</li>';
    return `${html}</ol>`;
}

// ---------------------------------------------------------------------------------------------
// Chrome: Mermaid diagrams and PNG icons
// ---------------------------------------------------------------------------------------------

const DIAGRAM_THEMES = {
    light: {
        primaryColor: '#eef2ff', primaryBorderColor: '#a5b4fc', primaryTextColor: '#0f172a',
        lineColor: '#64748b', textColor: '#0f172a', clusterBkg: '#f8fafc', clusterBorder: '#cbd5e1',
        edgeLabelBackground: '#ffffff', noteBkgColor: '#f8fafc', noteBorderColor: '#cbd5e1', noteTextColor: '#0f172a',
        actorBkg: '#eef2ff', actorBorder: '#a5b4fc', actorTextColor: '#0f172a', actorLineColor: '#94a3b8',
        signalColor: '#475569', signalTextColor: '#0f172a', labelBoxBkgColor: '#eef2ff', labelBoxBorderColor: '#a5b4fc',
        labelTextColor: '#0f172a', loopTextColor: '#0f172a', activationBkgColor: '#e0e7ff', activationBorderColor: '#a5b4fc',
        sequenceNumberColor: '#ffffff',
    },
    dark: {
        darkMode: true, background: '#111827',
        primaryColor: '#1e1b4b', primaryBorderColor: '#4f46e5', primaryTextColor: '#e5e7eb',
        lineColor: '#94a3b8', textColor: '#e5e7eb', clusterBkg: '#0f172a', clusterBorder: '#334155',
        edgeLabelBackground: '#111827', noteBkgColor: '#0f172a', noteBorderColor: '#334155', noteTextColor: '#e5e7eb',
        actorBkg: '#1e1b4b', actorBorder: '#4f46e5', actorTextColor: '#e5e7eb', actorLineColor: '#475569',
        signalColor: '#94a3b8', signalTextColor: '#e5e7eb', labelBoxBkgColor: '#1e1b4b', labelBoxBorderColor: '#4f46e5',
        labelTextColor: '#e5e7eb', loopTextColor: '#e5e7eb', activationBkgColor: '#312e81', activationBorderColor: '#4f46e5',
        sequenceNumberColor: '#111827',
    },
};

/** Runs the callback with a page of a headless Chrome; returns null when there is no Chrome. */
async function withChrome(callback) {
    if (!existsSync(CHROME)) {
        console.warn(`! Chrome not found at "${CHROME}" (set CHROME_PATH): diagrams stay code blocks, PNG icons are kept.`);
        return null;
    }
    const { default: puppeteer } = await import('puppeteer-core');
    const browser = await puppeteer.launch({ executablePath: CHROME, headless: true });
    try {
        return await callback(await browser.newPage());
    } finally {
        await browser.close();
    }
}

/** Draws every diagram once per theme, with the font of the site so that the labels fit their boxes. */
async function drawDiagrams(page, diagrams, font) {
    await page.setContent(`<!doctype html><style>
        @font-face { font-family: "Inter"; font-weight: 100 900; src: url(data:font/woff2;base64,${font.toString('base64')}) format("woff2"); }
        body { font-family: "Inter", sans-serif; }</style><body></body>`);
    await page.addScriptTag({ path: path.join(toolsDir, 'node_modules/mermaid/dist/mermaid.min.js') });
    await page.evaluate(() => document.fonts.load('16px "Inter"'));

    return page.evaluate(async (sources, themes) => {
        const drawn = [];
        for (const [index, text] of sources.entries()) {
            const variants = {};
            for (const [name, themeVariables] of Object.entries(themes)) {
                window.mermaid.initialize({
                    startOnLoad: false,
                    theme: 'base',
                    themeVariables: { ...themeVariables, fontFamily: '"Inter", system-ui, sans-serif', fontSize: '14px' },
                });
                variants[name] = (await window.mermaid.render(`diagram-${index}-${name}`, text)).svg;
            }
            drawn.push(variants);
        }
        return drawn;
    }, diagrams.map(d => d.source), DIAGRAM_THEMES);
}

async function drawIcons(page, svg) {
    for (const [file, size] of [['assets/favicon-32.png', 32], ['assets/apple-touch-icon.png', 180]]) {
        await page.setViewport({ width: size, height: size });
        await page.setContent(`<!doctype html><style>html, body { margin: 0; } svg { display: block; width: ${size}px; height: ${size}px; }</style>${svg}`);
        write(file, await page.screenshot({ type: 'png', omitBackground: true }));
    }
}

// ---------------------------------------------------------------------------------------------
// Page parts
// ---------------------------------------------------------------------------------------------

const ICONS = {
    questions: '<rect x="3" y="4" width="6" height="6" rx="1.5"/><path d="M13 7h8M13 17h8"/><circle cx="6" cy="17" r="3"/>',
    logic: '<circle cx="6" cy="5" r="2"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="12" r="2"/><path d="M6 7v10M6 12h10"/>',
    pages: '<path d="m12 3-9 5 9 5 9-5-9-5z"/><path d="m3 13 9 5 9-5"/>',
    chart: '<path d="M4 4v16h16"/><path d="M8 16v-5M12 16V8M16 16v-3"/>',
    download: '<path d="M12 4v11M7 11l5 5 5-5M5 20h14"/>',
    workspaces: '<rect x="3" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5"/>',
    share: '<circle cx="6" cy="12" r="2.5"/><circle cx="18" cy="6" r="2.5"/><circle cx="18" cy="18" r="2.5"/><path d="m8.2 10.8 7.6-3.6M8.2 13.2l7.6 3.6"/>',
    shield: '<path d="M12 3 5 6v5c0 4.5 2.9 8.3 7 10 4.1-1.7 7-5.5 7-10V6l-7-3z"/><path d="m9 12 2 2 4-4"/>',
    lock: '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
    account: '<circle cx="12" cy="8" r="4"/><path d="M4 20c0-3.3 3.6-6 8-6s8 2.7 8 6"/>',
    people: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c0-3 2.9-5.5 6.5-5.5s6.5 2.5 6.5 5.5"/><path d="M16 4.8a3.5 3.5 0 0 1 0 6.4M18 14.9c2.1.8 3.5 2.7 3.5 5.1"/>',
    api: '<path d="m8 8-4 4 4 4M16 8l4 4-4 4M13.5 5l-3 14"/>',
    book: '<path d="M5 4h11a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3V4z"/><path d="M5 17a3 3 0 0 1 3-3h11"/>',
    pencil: '<path d="M4 20h4L19.5 8.5a2.1 2.1 0 0 0-3-3L5 17v3z"/><path d="m14.5 7.5 2 2"/>',
    inbox: '<path d="M4 13 6.5 5h11L20 13v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-5z"/><path d="M4 13h4.5a3.5 3.5 0 0 0 7 0H20"/>',
    template: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/>',
    palette: '<path d="M12 3a9 9 0 1 0 0 18c1.4 0 2-1 2-2 0-1.6-1.1-1.8-1.1-3 0-1 .8-1.8 2-1.8H17a4 4 0 0 0 4-4c0-4-4-7.2-9-7.2z"/><circle cx="7.5" cy="11.5" r="1"/><circle cx="10.5" cy="7.5" r="1"/><circle cx="15" cy="7.5" r="1"/>',
    devices: '<rect x="3" y="4" width="13" height="10" rx="1.5"/><path d="M7 18h5M9.5 14v4"/><rect x="15" y="9" width="6" height="11" rx="1.5"/>',
    validate: '<path d="M9 4H6a1 1 0 0 0-1 1v15a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V5a1 1 0 0 0-1-1h-3"/><rect x="9" y="2.5" width="6" height="3.5" rx="1"/><path d="m9 13.5 2 2 4-4.5"/>',
    history: '<path d="M4 12a8 8 0 1 0 2.6-5.9L4 8.5"/><path d="M4 4v4.5h4.5M12 8v4.5l3 1.8"/>',
    key: '<circle cx="8" cy="15" r="4"/><path d="m11 12 8.5-8.5M16 7l2.5 2.5M18.5 4.5 21 7"/>',
    search: '<circle cx="11" cy="11" r="6.5"/><path d="m16 16 4.5 4.5"/>',
    up: '<path d="M12 19V5M6 11l6-6 6 6"/>',
    check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
    arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
    external: '<path d="M14 5h5v5M19 5l-8 8M11 6H6a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h11a1 1 0 0 0 1-1v-5"/>',
    menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
    close: '<path d="m6 6 12 12M18 6 6 18"/>',
    left: '<path d="m14 6-6 6 6 6"/>',
    right: '<path d="m10 6 6 6-6 6"/>',
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/>',
    moon: '<path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5z"/>',
};
const icon = name => `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name]}</svg>`;

// The product tour: tab, heading, text, three points, screenshot (file, alternative text) and the link to the documentation.
const TOUR = [
    {
        id: 'build', icon: 'pencil', label: 'Build',
        title: 'Design surveys the way you think',
        text: 'Add pages and questions, set the rules and preview the survey exactly as respondents will see it — before a single person answers.',
        points: [
            'Ten question types, from text and choices to star ratings and NPS scales',
            'Show or hide questions and whole pages with All/Any conditions',
            'Start from a template, duplicate a survey or import one as JSON',
        ],
        shot: ['builder', 'The survey builder with a page of questions and the outline of the survey'],
        link: ['docs.html#9-designing-surveys', 'How surveys are designed'],
    },
    {
        id: 'share', icon: 'share', label: 'Share',
        title: 'Reach people wherever they are',
        text: 'Publish the survey and hand it out in the way that suits your audience. Open and close it by hand, on a schedule or after a number of responses.',
        points: [
            'A link, a QR code and a ready-made invitation text',
            'An embed snippet for any website',
            'Anonymous or signed-in answers, optionally behind a password',
        ],
        shot: ['share', 'The share page of a survey with its link, QR code and embed snippet'],
        link: ['docs.html#11-collecting-responses', 'How responses are collected'],
    },
    {
        id: 'collect', icon: 'inbox', label: 'Collect',
        title: 'A survey people actually finish',
        text: 'Respondents get a clean, mobile-friendly page that only asks what applies to them and tells them right away when something is missing.',
        points: [
            'Progress bar, required markers and live validation',
            'Signed-in respondents can save a draft and continue later',
            'Invisible bot and spam protection — no CAPTCHA',
        ],
        shot: ['survey', 'A survey page with a progress bar and required questions'],
        link: ['docs.html#151-respondents', 'What respondents see'],
    },
    {
        id: 'analyse', icon: 'chart', label: 'Analyse',
        title: 'Reports that are ready to share',
        text: 'Generate an overview report with one click, then shape it with the report builder. Charts and tables always show the live data.',
        points: [
            'KPIs, bar, pie, doughnut and line charts, cross-tabs and NPS',
            'Date and answer filters with a live preview',
            'Export as PDF, Excel, CSV, plain text or JSON',
        ],
        shot: ['report', 'A report with key figures, charts and tables'],
        link: ['docs.html#12-reporting', 'How reporting works'],
    },
    {
        id: 'manage', icon: 'workspaces', label: 'Manage',
        title: 'One installation, many teams',
        text: 'Workspaces keep the surveys, responses and reports of every team, class or client apart — on a single installation that you control.',
        points: [
            'Each workspace has its own admins, members and join link',
            'Super admins approve, disable and manage workspaces without seeing their content',
            'Audit log, role management and your own branding',
        ],
        shot: ['system-overview', 'The System console with the workspaces of the installation'],
        link: ['docs.html#53-workspaces-multi-tenancy', 'How workspaces are isolated'],
    },
];

const FEATURES = [
    ['questions', '10 question types', 'Short and long text, single and multiple choice, dropdowns, numbers, e-mail, dates, star ratings and scales — with “Other → free text” options.'],
    ['logic', 'Conditional logic', 'Show or hide questions and whole pages based on earlier answers. Evaluated live in the browser and checked again on the server.'],
    ['pages', 'Multi-page surveys', 'Split long surveys into pages with a progress bar, per-page validation and a thank-you page at the end.'],
    ['chart', 'Report builder', 'Ten widget types — KPIs, charts, cross-tabs, NPS, text answers and raw grids — with filters and a live preview.'],
    ['download', 'Exports', 'Download reports as PDF, Excel, CSV, plain text or JSON, and the raw responses as CSV, Excel or JSON.'],
    ['template', 'Templates', 'New workspaces start with three ready-made surveys. Duplicate any survey, or move one between installations as JSON.'],
    ['workspaces', 'Workspaces', 'One installation hosts many fully isolated workspaces. Anyone can create one — with approval, if you prefer.'],
    ['palette', 'Your branding', 'Set the product name, the tagline and your own logo for the whole site, with a live preview.'],
    ['devices', 'Any device, light or dark', 'Every page is responsive and has a dark mode. Surveys are comfortable to answer on a phone.'],
    ['api', 'REST API', 'Every capability is available over <code>/api/v1</code> with bearer tokens, Swagger and ProblemDetails errors.'],
    ['book', 'Built-in user guide', 'Step-by-step help in plain language inside the app, for respondents and for administrators.'],
    ['account', 'Self-service accounts', 'People change their name, e-mail and password themselves, and can download or delete their personal data.'],
];

const STACK = ['ASP.NET Core 10', 'Blazor', 'EF Core 10', 'PostgreSQL 16', 'ASP.NET Core Identity', 'QuestPDF', 'Docker'];

const SAFEGUARDS = [
    ['lock', 'Encryption at rest', 'Written answers, “Other” texts and browser details are encrypted in the database with AES-256. The keys are kept outside the database.'],
    ['workspaces', 'Isolated workspaces', 'Isolation is enforced in the data layer. Super admins manage workspaces, but cannot open their surveys, answers or reports.'],
    ['shield', 'Bot and spam protection', 'A proof-of-work challenge, a minimum answering time, a honeypot field and rate limits — without a CAPTCHA or a third-party service.'],
    ['key', 'Two-factor sign-in', 'Works with any authenticator app and comes with recovery codes. Accounts are locked after repeated failed sign-ins.'],
    ['validate', 'Checked on the server', 'Every survey design and every answer is validated on the server, and the conditional logic is evaluated there again.'],
    ['history', 'Audit log', 'Each workspace has its own audit log. Super admin actions and sign-ups are recorded in a separate one.'],
];

const ROLES = [
    ['account', 'Respondents', 'Answer in a couple of minutes, on any device.', [
        'Only the questions that apply to them',
        'A progress bar and clear, immediate validation',
        'Their own responses in one place when signed in',
    ]],
    ['people', 'Workspace admins', 'Run the surveys of a team, a class or a company.', [
        'Build, publish, schedule and close surveys',
        'Browse responses, build reports and export them',
        'Manage members, roles and the join link',
    ]],
    ['shield', 'Super admins', 'Look after the whole installation from the System console.', [
        'Create, approve, disable and delete workspaces',
        'Manage every account, the branding and the settings',
        'See figures only — never a workspace’s content',
    ]],
];

// More screens for the gallery: file (without extension), title, caption, alternative text
const SHOTS = [
    ['builder-logic', 'Display logic', 'Show or hide questions based on earlier answers', 'The display logic editor of a question in the survey builder'],
    ['preview', 'Preview', 'The survey exactly as respondents will see it', 'The preview of a survey'],
    ['report-builder', 'Report builder', 'Widgets and filters with a live preview', 'The report builder with its widget list and a live preview'],
    ['responses', 'Responses', 'Filter, open, delete and export the raw data', 'The list of responses of a survey with filters and export buttons'],
    ['workspace-settings', 'Workspace settings', 'Name, address and the join link for new members', 'The settings of a workspace with its join link'],
    ['users', 'Members', 'Roles, new passwords and the join link', 'The list of members of a workspace'],
    ['guide', 'User guide', 'Plain-language help inside the app', 'The built-in user guide'],
    ['dashboard-dark', 'Dark mode', 'Every page, remembered per browser', 'The admin dashboard in dark mode'],
];

const QUESTIONS = [
    ['Is there a hosted version of SmartSurvey?',
        '<p>No. SmartSurvey is self-hosted: you run it on your own computer or server, and your data stays there. With Docker that is a single command.</p>'],
    ['What do I need to run it?',
        '<p>Only Docker. The stack contains the web app and a PostgreSQL 16 database. Without Docker you need the .NET 10 SDK and PostgreSQL 16 — or SQLite for a quick demo. See <a href="docs.html#4-getting-started">Getting started</a>.</p>'],
    ['May I use it at work?',
        `<p>SmartSurvey is free for personal use, study, research, hobby projects and non-commercial organisations such as charities, schools and public institutions. Commercial use needs a separate licence from the repository owner — get in touch via <a href="${REPO_URL}">GitHub</a>. The details are in the <a href="${REPO_URL}/blob/main/LICENSE.md">PolyForm Noncommercial License 1.0.0</a>.</p>`],
    ['Do respondents need an account?',
        '<p>Not always. Each survey decides whether anonymous answers are allowed. Surveys that require an account are for the members of the workspace that runs them, who join with the workspace’s join link.</p>'],
    ['Can several teams share one installation?',
        '<p>Yes. Each team gets its own workspace with its own admins, members, surveys, responses and reports. Workspaces are completely separate, and even super admins cannot open a workspace’s content.</p>'],
    ['How do I back up and update it?',
        '<p>Back up the database <strong>together with</strong> the <code>keys</code> volume: it holds the keys that decrypt the written answers. To update, run <code>git pull &amp;&amp; docker compose up -d --build</code>; database migrations are applied when the app starts. See <a href="docs.html#19-deployment">Deployment</a>.</p>'],
    ['Is there an API?',
        '<p>Yes. Everything the user interface does is available over <code>/api/v1</code> with bearer tokens, documented with Swagger. See <a href="docs.html#14-rest-api">REST API</a>.</p>'],
];

/** Width and height of a WebP file (VP8X header, which every guide screenshot has). */
function webpSize(bytes) {
    if (bytes.toString('latin1', 12, 16) !== 'VP8X') return null;
    return { width: bytes.readUIntLE(24, 3) + 1, height: bytes.readUIntLE(27, 3) + 1 };
}

function layout({ file, title, description, current, body, site }) {
    const home = file === 'index.html' ? '' : 'index.html';
    const links = [
        [`${home}#tour`, 'Product'],
        [`${home}#features`, 'Features'],
        [`${home}#security`, 'Security'],
        ['docs.html', 'Documentation'],
        ['changelog.html', 'Changelog'],
    ];
    const footer = [
        ['Product', [[`${home}#tour`, 'Product tour'], [`${home}#features`, 'Features'], [`${home}#security`, 'Security'], [`${home}#screenshots`, 'Screenshots']]],
        ['Resources', [[`${home}#quick-start`, 'Quick start'], ['docs.html', 'Documentation'], ['changelog.html', 'Changelog'], [`${home}#questions`, 'Questions']]],
        ['Project', [[REPO_URL, 'Source code'], [`${REPO_URL}/issues`, 'Report an issue'], [`${REPO_URL}/blob/main/LICENSE.md`, 'Licence'], [COFFEE_URL, 'Buy me a coffee']]],
    ];
    const canonical = SITE_URL + (file === 'index.html' ? '' : file);

    return `<!doctype html>
<!-- Generated by scripts/pages/build.mjs. Do not edit this file: change the sources and build again. -->
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
${file === '404.html' ? `<base href="${SITE_PATH}">\n<meta name="robots" content="noindex">` : `<link rel="canonical" href="${canonical}">`}
<title>${esc(title)}</title>
<meta name="description" content="${esc(description)}">
<meta property="og:type" content="website">
<meta property="og:site_name" content="SmartSurvey">
<meta property="og:title" content="${esc(title)}">
<meta property="og:description" content="${esc(description)}">
<meta property="og:url" content="${canonical}">
<meta property="og:image" content="${SITE_URL}img/dashboard.webp">
<meta name="twitter:card" content="summary_large_image">
<meta name="theme-color" content="#4f46e5">
<link rel="icon" href="assets/favicon.svg" type="image/svg+xml">
<link rel="icon" href="assets/favicon-32.png" sizes="32x32" type="image/png">
<link rel="apple-touch-icon" href="assets/apple-touch-icon.png">
<script>(function(){var d=document.documentElement,t;try{t=localStorage.getItem('ss-site-theme')}catch(e){}d.dataset.theme=t==='dark'||t==='light'?t:(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light');d.classList.add('js')})()</script>
<link rel="preload" href="assets/fonts/${FONT_FILE}" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="assets/site.css">
</head>
<body>
<a class="skip-link" href="#main">Skip to the content</a>
<header class="site-header">
  <div class="container">
    <a class="brand" href="${home || '#top'}" aria-label="SmartSurvey home">${site.mark}<span>SmartSurvey</span></a>
    <nav class="site-nav" id="site-nav" aria-label="Main">
      ${links.map(([href, text]) => `<a href="${href}"${text === current ? ' aria-current="page"' : ''}>${text}</a>`).join('\n      ')}
    </nav>
    <div class="header-actions">
      <a class="header-link" href="${REPO_URL}">GitHub ${icon('external')}</a>
      <button class="icon-btn" type="button" data-theme-toggle aria-label="Switch between light and dark mode">
        <span class="only-light">${icon('moon')}</span><span class="only-dark">${icon('sun')}</span>
      </button>
      <a class="btn btn-primary btn-sm" href="${home}#quick-start">Get started</a>
      <button class="icon-btn nav-toggle" type="button" data-nav-toggle aria-controls="site-nav" aria-expanded="false" aria-label="Menu">${icon('menu')}</button>
    </div>
  </div>
</header>
<main id="main">
${body}
</main>
<footer class="site-footer">
  <div class="container">
    <div class="footer-grid">
      <div class="footer-about">
        <a class="brand" href="${home || '#top'}">${site.mark.replaceAll('ss-mark', 'ss-mark-footer')}<span>SmartSurvey</span></a>
        <p>Self-hosted surveys with conditional logic, workspaces and reports you can export. Free for non-commercial use.</p>
      </div>
      ${footer.map(([heading, items]) => `<nav aria-label="${heading}">
        <h2>${heading}</h2>
        <ul>
          ${items.map(([href, text]) => `<li><a href="${href}">${text}</a></li>`).join('\n          ')}
        </ul>
      </nav>`).join('\n      ')}
    </div>
    <div class="footer-bottom">
      <p>© 2026 Supun · SmartSurvey ${esc(site.version)}</p>
      <p>Source-available under the <a href="${REPO_URL}/blob/main/LICENSE.md">PolyForm Noncommercial License 1.0.0</a></p>
    </div>
  </div>
</footer>
<script src="assets/site.js" defer></script>
</body>
</html>
`;
}

function indexPage(site) {
    const ticks = points => `<ul class="ticks">
          ${points.map(point => `<li>${icon('check')}${esc(point)}</li>`).join('\n          ')}
        </ul>`;

    // A screenshot in a window frame that opens in the viewer.
    const framed = (file, alt, title) => {
        const { width, height } = site.shots.get(file);
        return `<button class="zoom" type="button" data-shot="img/${file}.webp" data-title="${esc(title)}" aria-label="Enlarge the screenshot: ${esc(title)}">
          <span class="window"><span class="window-bar" aria-hidden="true"><span></span><span></span><span></span></span>
          <img src="img/${file}.webp" alt="${esc(alt)}" width="${width}" height="${height}" loading="lazy" decoding="async"></span>
        </button>`;
    };

    const panel = ({ id, label, title, text, points, shot: [file, alt], link: [href, linkText] }, index) => `<div class="tour-panel" id="tour-${id}" role="tabpanel" aria-labelledby="tab-${id}">
      <div>
        <span class="tour-step">Step ${index + 1} · ${label}</span>
        <h3>${esc(title)}</h3>
        <p>${esc(text)}</p>
        ${ticks(points)}
        <a class="text-link" href="${href}">${esc(linkText)} ${icon('arrow')}</a>
      </div>
      ${framed(file, alt, `${label} — ${title}`)}
    </div>`;

    const shot = ([file, title, caption, alt]) => {
        const { width, height } = site.shots.get(file);
        return `<figure class="card shot">
        <button type="button" data-shot="img/${file}.webp" data-title="${esc(`${title} — ${caption}`)}" aria-label="Enlarge the screenshot: ${esc(title)}">
          <img src="img/${file}.webp" alt="${esc(alt)}" width="${width}" height="${height}" loading="lazy" decoding="async">
        </button>
        <figcaption><strong>${esc(title)}</strong>${esc(caption)}</figcaption>
      </figure>`;
    };

    return `<section class="hero" id="top">
  <div class="container">
    <a class="pill" href="changelog.html"><b>Version ${esc(site.version)}</b>See what is new ${icon('arrow')}</a>
    <h1>Ask better questions. <span class="gradient-text">Get answers you can use.</span></h1>
    <p class="lead">SmartSurvey is a self-hosted survey platform. Build surveys with conditional logic, collect responses on any device and turn them into clear reports — ready to export as PDF, Excel or CSV.</p>
    <div class="btn-row">
      <a class="btn btn-primary btn-lg" href="#quick-start">Get started ${icon('arrow')}</a>
      <a class="btn btn-light btn-lg" href="${REPO_URL}">View on GitHub</a>
    </div>
    <ul class="checks">
      <li>${icon('check')}Runs with one Docker command</li>
      <li>${icon('check')}Your data stays on your server</li>
      <li>${icon('check')}Free for non-commercial use</li>
    </ul>
    <div class="hero-visual">
      <div class="window">
        <div class="window-bar" aria-hidden="true"><span></span><span></span><span></span><em>surveys.example.com/admin</em><i></i></div>
        <img class="only-light" src="img/dashboard.webp" width="1280" height="800" fetchpriority="high" alt="The SmartSurvey admin dashboard with key numbers, a 30-day chart, the top surveys and the latest responses">
        <img class="only-dark" src="img/dashboard-dark.webp" width="1280" height="800" alt="The SmartSurvey admin dashboard in dark mode">
      </div>
      <div class="phone"><img src="img/survey-mobile.webp" width="780" height="1688" alt="A survey on a phone"></div>
    </div>
  </div>
</section>

<div class="stats-band">
  <div class="container">
    <ul class="stats">
      <li><strong>10</strong><span>question types</span></li>
      <li><strong>5</strong><span>export formats</span></li>
      <li><strong>900+</strong><span>automated tests</span></li>
      <li><strong>.NET 10</strong><span>long-term support</span></li>
    </ul>
  </div>
</div>

<section class="section" id="tour">
  <div class="container">
    <div class="section-head reveal">
      <span class="eyebrow">Product tour</span>
      <h2>From the first question to the final report</h2>
      <p>Everything happens in one place: no spreadsheets, no copy and paste, no second tool for the charts.</p>
    </div>
    <div class="tour-tabs reveal">
      <div role="tablist" aria-label="Product tour" data-tabs>
        ${TOUR.map(({ id, label, icon: name }) => `<button class="tour-tab" type="button" role="tab" id="tab-${id}" aria-controls="tour-${id}" aria-selected="false">${icon(name)}${label}</button>`).join('\n        ')}
      </div>
    </div>
    ${TOUR.map(panel).join('\n    ')}
  </div>
</section>

<section class="section section-alt" id="features">
  <div class="container">
    <div class="section-head reveal">
      <span class="eyebrow">Features</span>
      <h2>Everything included, nothing to add on</h2>
      <p>No plans, no locked features and no per-response pricing. What you install is the whole product.</p>
    </div>
    <div class="feature-grid">
      ${FEATURES.map(([name, title, text]) => `<article class="card feature reveal">
        <div class="feature-icon">${icon(name)}</div>
        <h3>${title}</h3>
        <p>${text}</p>
      </article>`).join('\n      ')}
    </div>
    <ul class="stack reveal" aria-label="Technology">
      <li>Built with</li>
      ${STACK.map(item => `<li>${item}</li>`).join('\n      ')}
    </ul>
  </div>
</section>

<section class="band" id="security">
  <div class="container split">
    <div class="split-head reveal">
      <span class="eyebrow">Security and privacy</span>
      <h2>Your respondents’ answers stay yours</h2>
      <p>SmartSurvey runs on your own server, so what people tell you stays with you — protected in the database, in the browser and on the way in.</p>
      <a class="text-link" href="docs.html#16-security">Read the security chapter ${icon('arrow')}</a>
    </div>
    <ul class="safe-grid reveal">
      ${SAFEGUARDS.map(([name, title, text]) => `<li>${icon(name)}<h3>${title}</h3><p>${text}</p></li>`).join('\n      ')}
    </ul>
  </div>
</section>

<section class="section" id="roles">
  <div class="container">
    <div class="section-head reveal">
      <span class="eyebrow">For everyone involved</span>
      <h2>Simple for respondents, complete for administrators</h2>
      <p>Three roles, each with exactly the pages it needs.</p>
    </div>
    <div class="roles">
      ${ROLES.map(([name, title, text, points]) => `<article class="card role reveal">
        <div class="role-icon">${icon(name)}</div>
        <h3>${title}</h3>
        <p>${text}</p>
        ${ticks(points)}
      </article>`).join('\n      ')}
    </div>
  </div>
</section>

<section class="section section-alt" id="screenshots">
  <div class="container">
    <div class="section-head reveal">
      <span class="eyebrow">Screenshots</span>
      <h2>See it before you install it</h2>
      <p>Taken from a fresh demo instance. Select a picture to enlarge it.</p>
    </div>
    <div class="shots">
      ${SHOTS.map(shot).join('\n      ')}
    </div>
  </div>
</section>

<section class="section" id="quick-start">
  <div class="container quickstart">
    <div class="split-head reveal">
      <span class="eyebrow">Quick start</span>
      <h2>Up and running in a few minutes</h2>
      <p>The Docker stack contains the web app and a PostgreSQL 16 database. Your data is kept in Docker volumes and survives restarts.</p>
      <ol class="steps">
        <li>
          <h3>Install Docker</h3>
          <p><a href="https://www.docker.com/products/docker-desktop/">Docker Desktop</a> on Windows and macOS, or <a href="https://docs.docker.com/engine/install/">Docker Engine</a> with the Compose plugin on Linux.</p>
        </li>
        <li>
          <h3>Get the code and start it</h3>
          <p>Three commands. The first start takes a few minutes, later starts take seconds.</p>
        </li>
        <li>
          <h3>Open the app and sign in</h3>
          <p>The demo data gives you example surveys, about 200 responses and a sample report to explore.</p>
        </li>
      </ol>
    </div>
    <div class="reveal">
      <div class="terminal">
        <div class="terminal-bar" aria-hidden="true"><span></span><span></span><span></span><em>Terminal</em></div>
        <pre><code><span class="cmd">git clone ${REPO_URL}.git</span>
<span class="cmd">cd SmartSurvay</span>
<span class="cmd">docker compose up -d --build</span>
<span class="note"># then open http://localhost:8080</span></code></pre>
      </div>
      <div class="card signin">
        <h3>First sign-in</h3>
        <table>
          <tr><th scope="row">Workspace admin</th><td><code>admin@smartsurvey.local</code></td></tr>
          <tr><th scope="row">Super admin</th><td><code>superadmin@smartsurvey.local</code></td></tr>
          <tr><th scope="row">Password</th><td><code>ChangeMe123!</code></td></tr>
        </table>
        <p>Change both passwords right after signing in, or set <code>ADMIN_PASSWORD</code> and <code>SUPERADMIN_PASSWORD</code> in <code>.env</code> before the first start. Going live? Follow the <a href="docs.html#19-deployment">deployment checklist</a>.</p>
      </div>
    </div>
  </div>
</section>

<section class="section section-alt" id="questions">
  <div class="container">
    <div class="section-head reveal">
      <span class="eyebrow">Questions</span>
      <h2>Good to know before you start</h2>
    </div>
    <div class="faq reveal">
      ${QUESTIONS.map(([question, answer]) => `<details>
        <summary>${esc(question)}</summary>
        <div>${answer}</div>
      </details>`).join('\n      ')}
    </div>
  </div>
</section>

<section class="section">
  <div class="container">
    <div class="cta reveal">
      <h2>Ready to hear from your audience?</h2>
      <p>Install SmartSurvey with one command and publish your first survey today.</p>
      <div class="btn-row">
        <a class="btn btn-white btn-lg" href="#quick-start">Get started ${icon('arrow')}</a>
        <a class="btn btn-ghost btn-lg" href="docs.html">Read the documentation</a>
      </div>
      <small>Free for non-commercial use under the <a href="${REPO_URL}/blob/main/LICENSE.md">PolyForm Noncommercial License 1.0.0</a>. Commercial use needs a separate licence.</small>
    </div>
  </div>
</section>

<dialog class="lightbox" id="lightbox" aria-label="Screenshot">
  <div class="lightbox-body">
    <div class="lightbox-img"><img src="" alt=""></div>
    <div class="lightbox-bar">
      <p data-caption></p>
      <button class="icon-btn" type="button" data-prev aria-label="Previous screenshot">${icon('left')}</button>
      <button class="icon-btn" type="button" data-next aria-label="Next screenshot">${icon('right')}</button>
      <button class="icon-btn" type="button" data-close aria-label="Close">${icon('close')}</button>
    </div>
  </div>
</dialog>`;
}

/** The band under the header of the documentation and the changelog: where you are, title, introduction, facts. */
function pageHead({ crumb, title, lead, meta }) {
    return `<div class="page-head">
  <div class="container">
    <nav class="crumbs" aria-label="Breadcrumb"><a href="index.html">SmartSurvey</a><span aria-hidden="true">/</span><span aria-current="page">${esc(crumb)}</span></nav>
    <h1>${esc(title)}</h1>
    <div class="page-lead">${lead}</div>
    <ul class="meta">
      ${meta.map(item => `<li>${item}</li>`).join('\n      ')}
    </ul>
  </div>
</div>
<div class="read-progress" aria-hidden="true"><span></span></div>`;
}

const toTopButton = `<button class="icon-btn to-top" type="button" data-to-top aria-label="Back to the top" hidden>${icon('up')}</button>`;

/** The documentation: its title and introduction move into the band, the chapters get a searchable list. */
function docsPage({ html, headings }, site) {
    const title = headings.find(h => h.depth === 1)?.text.replace(/^SmartSurvey\s*[—–-]\s*/, '') ?? 'Documentation';
    let body = html.replace(/<h1 [^>]*>[\s\S]*?<\/h1>\n?/, '');
    let lead = '<p>What SmartSurvey does, how it is built and how to run, configure, extend, test and deploy it.</p>';
    const intro = body.match(/^\s*<blockquote>([\s\S]*?)<\/blockquote>\n?/);
    if (intro) {
        lead = intro[1];
        body = body.slice(intro[0].length);
    }
    body = body.replace(/^\s*<hr>\n?/, '');

    return `${pageHead({
        crumb: 'Documentation',
        title,
        lead,
        meta: [
            `Version ${esc(site.version)}`,
            `${headings.filter(h => h.depth === 2).length} chapters`,
            `<a href="${REPO_URL}/blob/main/docs/DOCUMENTATION.md">Source on GitHub ${icon('external')}</a>`,
        ],
    })}
<div class="container doc-layout">
  <aside class="doc-side">
    <div class="doc-search" role="search">
      ${icon('search')}
      <input type="search" id="doc-search" placeholder="Search the documentation" aria-label="Search the documentation" autocomplete="off" spellcheck="false">
      <kbd aria-hidden="true">/</kbd>
    </div>
    <div class="search-results" id="search-results" aria-live="polite" hidden></div>
    <details class="toc" open>
      <summary>Contents</summary>
      <p class="toc-title">Contents</p>
      ${tableOfContents(headings, 2, 3)}
    </details>
  </aside>
  <article class="prose">
${body}
  </article>
</div>
${toTopButton}`;
}

const CHANGE_TYPES = ['Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security'];
const longDate = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });

/** The changelog as a timeline: one entry per "## [version] - date" section, the kinds of change as labels. */
function changelogPage({ html, headings }) {
    const marks = [...html.matchAll(/<h2 id="([^"]*)">[\s\S]*?<\/h2>\n?/g)];
    const titles = headings.filter(h => h.depth === 2);
    const lead = html.slice(0, marks[0]?.index ?? html.length).replace(/<h1 [^>]*>[\s\S]*?<\/h1>\n?/, '').replace(/<hr>\n?/g, '');

    const releases = marks.map((mark, index) => {
        const [, name, date] = titles[index].text.match(/^\[?([^\]\s]+)\]?(?:\s*[-–—]\s*(\d{4}-\d{2}-\d{2}))?/);
        const end = marks[index + 1]?.index ?? html.length;
        const body = html.slice(mark.index + mark[0].length, end)
            .replace(/<hr>\n?/g, '')
            .replace(new RegExp(`<h3 id="([^"]+)">(${CHANGE_TYPES.join('|')})<a `, 'g'),
                (_, id, type) => `<h3 class="change change-${type.toLowerCase()}" id="${id}"><span>${type}</span><a `);
        return { slug: mark[1], name, date, body, id: date ? `v${name}` : name.toLowerCase() };
    });
    const latest = releases.find(release => release.date);

    return `${pageHead({
        crumb: 'Changelog',
        title: 'Changelog',
        lead,
        meta: [
            `${releases.filter(release => release.date).length} releases`,
            ...releases.map(release => `<a href="#${release.id}">${esc(release.name)}</a>`),
        ],
    })}
<div class="container timeline">
  ${releases.map(release => `<section class="release" id="${release.id}" aria-labelledby="${release.slug}">
    <header class="release-meta">
      <h2 id="${release.slug}">${esc(release.name)}</h2>
      ${release.date ? `<time datetime="${release.date}">${longDate.format(new Date(release.date))}</time>` : '<span>Not released yet</span>'}
      ${release === latest ? '<span class="tag">Latest</span>' : ''}
    </header>
    <div class="card release-body prose">
${release.body}
    </div>
  </section>`).join('\n  ')}
</div>
${toTopButton}`;
}

const notFoundPage = `<div class="container notfound">
  <div>
    <p class="notfound-code gradient-text" aria-hidden="true">404</p>
    <h1>This page does not exist</h1>
    <p>The address may be mistyped, or the page has moved. These will get you back on track:</p>
    <div class="btn-row">
      <a class="btn btn-primary" href="index.html">Go to the start page</a>
      <a class="btn btn-light" href="docs.html">Documentation</a>
      <a class="btn btn-light" href="changelog.html">Changelog</a>
    </div>
  </div>
</div>`;

// ---------------------------------------------------------------------------------------------
// Link check
// ---------------------------------------------------------------------------------------------

function checkLinks(pages) {
    const ids = new Map(pages.map(({ file, html }) => [file, new Set([...html.matchAll(/\sid="([^"]+)"/g)].map(m => m[1]))]));
    const problems = [];

    for (const { file, html } of pages) {
        if (file === '404.html') continue;
        for (const [, attribute, url] of html.matchAll(/\s(href|src|data-shot)="([^"]*)"/g)) {
            if (/^([a-z][a-z0-9+.-]*:|\/\/)/i.test(url) || url === '') continue;
            const [target, anchor] = unesc(url).split('#');
            const page = target || file;
            if (!existsSync(path.join(siteDir, page))) problems.push(`${file}: ${attribute}="${url}" — no such file`);
            else if (anchor && ids.has(page) && !ids.get(page).has(decodeURIComponent(anchor))) problems.push(`${file}: ${attribute}="${url}" — no such anchor`);
        }
    }

    if (problems.length) throw new Error(`Broken links:\n  ${problems.join('\n  ')}`);
}

// ---------------------------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------------------------

const version = source('Directory.Build.props').match(/<Version>([^<]+)<\/Version>/)?.[1];
if (!version) throw new Error('No <Version> in Directory.Build.props.');

const changelog = source('CHANGELOG.md').replace(/^## \[Unreleased\]\s*\n(?=## )/m, '');
const released = changelog.match(new RegExp(`^## \\[${version.replaceAll('.', '\\.')}\\] - (\\d{4}-\\d{2}-\\d{2})`, 'm'))?.[1];
if (!released) throw new Error(`CHANGELOG.md has no section for version ${version}.`);

// The sidebar replaces the table of contents of the document.
const documentation = source('docs/DOCUMENTATION.md').replace(/^## Table of contents\n[\s\S]*?(?=^## )/m, '');

// Screenshots: replace the whole folder, so pictures that were removed from the app disappear here too.
rmSync(path.join(siteDir, 'img'), { recursive: true, force: true });
const shots = new Map();
for (const file of readdirSync(path.join(repoDir, SHOTS_DIR)).filter(name => name.endsWith('.webp')).sort()) {
    const bytes = sourceBytes(`${SHOTS_DIR}/${file}`);
    write(`img/${file}`, bytes);
    shots.set(path.basename(file, '.webp'), webpSize(bytes));
}
for (const file of ['dashboard', 'dashboard-dark', 'survey-mobile', ...TOUR.map(step => step.shot[0]), ...SHOTS.map(shot => shot[0])]) {
    if (!shots.get(file)) throw new Error(`Screenshot ${SHOTS_DIR}/${file}.webp is missing (or is not a VP8X WebP file).`);
}

const font = sourceBytes(`${FONT_DIR}/${FONT_FILE}`);
write(`assets/fonts/${FONT_FILE}`, font);
write('assets/fonts/LICENSE-Inter.txt', sourceBytes(`${FONT_DIR}/LICENSE`));

const markSvg = readFileSync(path.join(siteDir, 'assets/favicon.svg'), 'utf8').trim();
const site = { version, released, shots, mark: markSvg.replace('<svg ', '<svg aria-hidden="true" ') };

const docs = renderMarkdown(documentation, 'docs');
const changes = renderMarkdown(changelog, '');

// Diagrams and icons need a browser; without one the site is still complete.
const drawn = await withChrome(async page => {
    const diagrams = docs.diagrams.length ? await drawDiagrams(page, docs.diagrams, font) : [];
    await drawIcons(page, markSvg);
    return diagrams;
});
docs.html = docs.html.replace(/<!--diagram:(\d+)-->/g, (_, index) => {
    const { source: text, label } = docs.diagrams[index];
    if (!drawn) return `<div class="code-wrap"><pre><code>${esc(text)}</code></pre></div>`;
    return `<figure class="diagram" role="img" aria-label="${esc(`Diagram: ${label}`)}">`
        + `<div class="only-light">${drawn[index].light}</div><div class="only-dark">${drawn[index].dark}</div></figure>`;
});

const description = 'SmartSurvey is a self-hosted survey platform with conditional logic, workspaces, dynamic reports and PDF, Excel and CSV export — built with ASP.NET Core 10, Blazor and PostgreSQL.';
const pages = [
    {
        file: 'index.html',
        title: 'SmartSurvey — surveys, conditional logic and reports, self-hosted',
        description,
        body: indexPage(site),
    },
    {
        file: 'docs.html',
        title: 'Documentation · SmartSurvey',
        description: 'The technical and user documentation of SmartSurvey: architecture, data model, conditional logic, reporting, exports, REST API, configuration, security, testing and deployment.',
        current: 'Documentation',
        body: docsPage(docs, site),
    },
    {
        file: 'changelog.html',
        title: 'Changelog · SmartSurvey',
        description: `Release notes of SmartSurvey. Latest version: ${version}, released ${released}.`,
        current: 'Changelog',
        body: changelogPage(changes),
    },
    {
        file: '404.html',
        title: 'Page not found · SmartSurvey',
        description: 'This page does not exist.',
        body: notFoundPage,
    },
].map(page => ({ file: page.file, html: layout({ ...page, site }) }));

for (const { file, html } of pages) write(file, html);

write('sitemap.xml', `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${['', 'docs.html', 'changelog.html'].map(file => `  <url><loc>${SITE_URL}${file}</loc><lastmod>${released}</lastmod></url>`).join('\n')}
</urlset>
`);
write('robots.txt', `User-agent: *\nAllow: /\n\nSitemap: ${SITE_URL}sitemap.xml\n`);

checkLinks(pages);

console.log(`Site of SmartSurvey ${version} (released ${released}) written to ${repoDir}:`);
for (const { file, html } of pages) console.log(`  ${file.padEnd(16)} ${(html.length / 1024).toFixed(0).padStart(4)} kB`);
console.log(`  img/             ${readdirSync(path.join(siteDir, 'img')).length.toString().padStart(4)} files`);
console.log(`  diagrams         ${drawn ? String(drawn.length).padStart(4) : '   0'} drawn`);
