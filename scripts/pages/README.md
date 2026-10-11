# Project website (GitHub Pages)

The website of SmartSurvey — <https://supunsarachitha.github.io/SmartSurvay/> — is a set of static pages: a start
page, the documentation and the changelog. GitHub Pages serves them from the **root of `main`**
(*Settings → Pages → Deploy from a branch → `main`, `/ (root)`*), so the generated pages are committed there.
`.nojekyll` in the root tells GitHub to serve the files as they are.

SmartSurvey itself is a server application and cannot run on GitHub Pages; the site presents the project.

## Rebuild

After every release, and whenever `docs/DOCUMENTATION.md`, `CHANGELOG.md` or the guide screenshots change:

```bash
cd scripts/pages
npm ci
npm run build
cd ../.. && git add -A && git commit -m "Website for SmartSurvey x.y.z"
```

The build reads the working tree and writes into the repository root. It needs Node.js 20 or newer and, for the two
diagrams of the documentation and the PNG icons, a local Chrome (`CHROME_PATH` if it is not in the default macOS
location). Without Chrome the diagrams are shown as code blocks and the existing icons are kept. It ends with a link
check and fails if a link or an anchor of the site points nowhere. Building twice gives identical files.

## What comes from where

| Path (repository root) | Source |
|---|---|
| `index.html`, `404.html` | templates and texts in `build.mjs`; version from `Directory.Build.props`, release date from `CHANGELOG.md` |
| `docs.html` | `docs/DOCUMENTATION.md` (links to other files of the repository lead to GitHub); its title and introduction become the band at the top, the search works on the page itself |
| `changelog.html` | `CHANGELOG.md`: every `## [version] - date` section becomes an entry of the timeline, the `### Added` / `Changed` / `Fixed` … headings become labels |
| `img/` | the guide screenshots, `src/SmartSurvey.Web/wwwroot/img/guide/` |
| `assets/fonts/` | Inter and its licence, `src/SmartSurvey.Web/wwwroot/lib/inter/` |
| `assets/favicon-32.png`, `assets/apple-touch-icon.png` | drawn from `assets/favicon.svg` |
| `sitemap.xml`, `robots.txt` | generated |
| `assets/site.css`, `assets/site.js`, `assets/favicon.svg` | **written by hand** — edit these directly |

Everything in the first seven rows is overwritten by the next build: change the source and build again.

## Preview

Serve the repository under the same path as on GitHub:

```bash
mkdir -p /tmp/pages && ln -sfn "$PWD" /tmp/pages/SmartSurvay     # in the repository root
python3 -m http.server -d /tmp/pages 8097                        # http://localhost:8097/SmartSurvay/
```

If the site moves to its own domain, change `SITE_URL` in `build.mjs` and rebuild (it is used for the canonical
links, the sitemap and the base address of the 404 page).

## Third-party material

* **Inter** (the font in `assets/fonts/`) — SIL Open Font License 1.1, see `assets/fonts/LICENSE-Inter.txt`.
* The build tools — marked (MIT), highlight.js (BSD-3-Clause), Mermaid (MIT) and puppeteer-core (Apache-2.0) — are only
  used to generate the pages; none of their code is part of the site.
* The brand mark, the icons, the styles and the script of the site are original work for this project. The site loads
  nothing from other servers.
