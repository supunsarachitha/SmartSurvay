// SmartSurvey project site: theme switch, mobile navigation, product tour, copy buttons, screenshot viewer,
// fade-in of the start page, and for the documentation the search, the "where am I" marker of the contents,
// the reading progress and the way back to the top. No dependencies; every part is optional progressive enhancement.
(function () {
    'use strict';

    const root = document.documentElement;

    // ---- Theme (the inline script in <head> has already applied the stored or system theme) ----
    const themeButton = document.querySelector('[data-theme-toggle]');
    if (themeButton) {
        const label = () => themeButton.setAttribute('aria-label',
            root.dataset.theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode');
        label();
        themeButton.addEventListener('click', () => {
            root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
            try { localStorage.setItem('ss-site-theme', root.dataset.theme); } catch { /* private mode */ }
            label();
        });
    }

    // ---- Mobile navigation ----
    const navButton = document.querySelector('[data-nav-toggle]');
    const nav = document.getElementById('site-nav');
    if (navButton && nav) {
        const close = () => { nav.classList.remove('open'); navButton.setAttribute('aria-expanded', 'false'); };
        navButton.addEventListener('click', () => {
            navButton.setAttribute('aria-expanded', String(nav.classList.toggle('open')));
        });
        nav.addEventListener('click', e => { if (e.target.closest('a')) close(); });
        document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
    }

    // ---- Product tour: tabs (without JavaScript the steps are simply listed one after the other) ----
    const tablist = document.querySelector('[data-tabs]');
    if (tablist) {
        const tabs = [...tablist.querySelectorAll('[role="tab"]')];
        const panels = tabs.map(tab => document.getElementById(tab.getAttribute('aria-controls')));
        const select = (index, focus) => {
            tabs.forEach((tab, i) => {
                tab.setAttribute('aria-selected', String(i === index));
                tab.tabIndex = i === index ? 0 : -1;
                panels[i].hidden = i !== index;
            });
            if (focus) tabs[index].focus();
        };

        tabs.forEach((tab, index) => tab.addEventListener('click', () => select(index)));
        tablist.addEventListener('keydown', e => {
            const current = tabs.indexOf(document.activeElement);
            const next = { ArrowRight: current + 1, ArrowLeft: current - 1, Home: 0, End: tabs.length - 1 }[e.key];
            if (current < 0 || next === undefined) return;
            e.preventDefault();
            select((next + tabs.length) % tabs.length, true);
        });
        select(0);
    }

    // ---- Fade-in of the parts of the start page ----
    const reveals = document.querySelectorAll('.reveal');
    if ('IntersectionObserver' in window) {
        const observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.classList.add('in');
                observer.unobserve(entry.target);
            }
        }, { rootMargin: '0px 0px -8% 0px' });
        reveals.forEach(element => observer.observe(element));
    } else {
        reveals.forEach(element => element.classList.add('in'));
    }

    // ---- Copy buttons on code blocks ----
    if (navigator.clipboard) {
        document.querySelectorAll('pre').forEach(pre => {
            // Blocks of the documentation already sit in a .code-wrap, with a bar when they name their language.
            let wrap = pre.parentElement;
            if (!wrap.classList.contains('code-wrap')) {
                wrap = document.createElement('div');
                wrap.className = 'code-wrap';
                pre.replaceWith(wrap);
                wrap.append(pre);
            }

            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'copy-btn';
            button.textContent = 'Copy';
            button.addEventListener('click', async () => {
                try {
                    await navigator.clipboard.writeText(pre.innerText.replace(/\n$/, ''));
                    button.textContent = 'Copied';
                    button.classList.add('done');
                    setTimeout(() => { button.textContent = 'Copy'; button.classList.remove('done'); }, 1600);
                } catch { /* clipboard blocked: leave the button as it is */ }
            });
            (wrap.querySelector('.code-head') ?? wrap).append(button);
        });
    }

    // ---- Screenshot viewer ----
    const lightbox = document.getElementById('lightbox');
    const shots = [...document.querySelectorAll('[data-shot]')];
    if (lightbox && shots.length && typeof lightbox.showModal === 'function') {
        const image = lightbox.querySelector('img');
        const caption = lightbox.querySelector('[data-caption]');
        let current = 0;

        const show = index => {
            current = (index + shots.length) % shots.length;
            const shot = shots[current];
            image.src = shot.dataset.shot;
            image.alt = shot.querySelector('img').alt;
            caption.textContent = shot.dataset.title;
        };

        shots.forEach((shot, index) => shot.addEventListener('click', () => { show(index); lightbox.showModal(); }));
        lightbox.querySelector('[data-prev]').addEventListener('click', () => show(current - 1));
        lightbox.querySelector('[data-next]').addEventListener('click', () => show(current + 1));
        lightbox.querySelector('[data-close]').addEventListener('click', () => lightbox.close());
        lightbox.addEventListener('click', e => { if (e.target === lightbox) lightbox.close(); });
        lightbox.addEventListener('keydown', e => {
            if (e.key === 'ArrowLeft') show(current - 1);
            if (e.key === 'ArrowRight') show(current + 1);
        });
    }

    // ---- Table of contents: mark the section being read; close the mobile list after a choice ----
    const toc = document.querySelector('.toc');
    if (toc) {
        const links = new Map([...toc.querySelectorAll('a[href^="#"]')]
            .map(a => [decodeURIComponent(a.hash.slice(1)), a]));
        const headings = [...links.keys()].map(id => document.getElementById(id)).filter(Boolean);
        const wide = window.matchMedia('(min-width: 62.01rem)');
        let active = null;
        let chapter = null;

        // The details element is the collapsible list on small screens and always open on wide ones.
        const syncOpen = () => { toc.open = wide.matches; };
        syncOpen();
        wide.addEventListener('change', syncOpen);
        toc.addEventListener('click', e => { if (e.target.closest('a') && !wide.matches) toc.open = false; });

        const mark = () => {
            const line = parseFloat(getComputedStyle(root).scrollPaddingTop) + 8;
            let found = headings[0];
            for (const heading of headings) {
                if (heading.getBoundingClientRect().top <= line) found = heading; else break;
            }
            const link = found && links.get(found.id);
            if (link === active) return;
            active?.classList.remove('active');
            active = link;
            active?.classList.add('active');
            const entry = active?.closest('.toc > ol > li') ?? null;
            if (entry !== chapter) {
                chapter?.classList.remove('current');
                chapter = entry;
                chapter?.classList.add('current');
            }
            if (!wide.matches || !active) return;
            // Keep the marked entry visible inside the list (not scrollIntoView: it would interrupt the page scroll).
            const row = active.getBoundingClientRect(), box = toc.getBoundingClientRect();
            if (row.top < box.top) toc.scrollTop -= box.top - row.top + 8;
            else if (row.bottom > box.bottom) toc.scrollTop += row.bottom - box.bottom + 8;
        };

        let queued = false;
        window.addEventListener('scroll', () => {
            if (queued) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; mark(); });
        }, { passive: true });
        mark();
    }

    // ---- Search in the documentation: every chapter and section is one entry, built from the page itself ----
    const searchInput = document.getElementById('doc-search');
    const resultsBox = document.getElementById('search-results');
    const article = document.querySelector('.prose');
    if (searchInput && resultsBox && article && toc) {
        let sections = null;

        const index = () => {
            sections = [];
            let chapter = '';
            let section = null;
            for (const node of article.children) {
                if (node.matches('h2, h3')) {
                    const title = node.textContent.replace(/#$/, '').trim();
                    if (node.tagName === 'H2') chapter = title;
                    section = { id: node.id, title, chapter: node.tagName === 'H3' ? chapter : '', text: '' };
                    sections.push(section);
                } else if (section && !node.matches('.diagram')) {
                    // Without the bar and the button of a code block.
                    section.text += ' ' + (node.matches('.code-wrap') ? node.querySelector('pre').textContent : node.textContent);
                }
            }
            for (const entry of sections) {
                entry.text = entry.text.replace(/\s+/g, ' ').trim();
                entry.lowerTitle = entry.title.toLowerCase();
                entry.lowerText = entry.text.toLowerCase();
            }
        };

        // Text with the search words marked, as nodes (never as HTML).
        const marked = (text, words) => {
            const fragment = document.createDocumentFragment();
            const pattern = new RegExp(`(${words.map(word => word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})`, 'gi');
            text.split(pattern).forEach((part, i) => {
                if (!part) return;
                if (i % 2) fragment.append(Object.assign(document.createElement('mark'), { textContent: part }));
                else fragment.append(part);
            });
            return fragment;
        };

        const excerpt = (entry, words) => {
            const at = Math.min(...words.map(word => entry.lowerText.indexOf(word)).filter(position => position >= 0));
            if (!Number.isFinite(at)) return entry.text.slice(0, 110) + (entry.text.length > 110 ? '…' : '');
            const start = Math.max(0, entry.text.lastIndexOf(' ', Math.max(0, at - 40)));
            const end = Math.min(entry.text.length, start + 130);
            return (start ? '…' : '') + entry.text.slice(start, end).trim() + (end < entry.text.length ? '…' : '');
        };

        const search = () => {
            const words = searchInput.value.toLowerCase().split(/\s+/).filter(Boolean);
            resultsBox.hidden = !words.length;
            toc.hidden = Boolean(words.length);
            if (!words.length) return;
            if (!sections) index();

            // Every word has to occur; a word in the title counts more than one in the text.
            const hits = [];
            for (const entry of sections) {
                let score = 0;
                for (const word of words) {
                    const inTitle = entry.lowerTitle.includes(word);
                    if (!inTitle && !entry.lowerText.includes(word)) { score = 0; break; }
                    score += inTitle ? 10 : 1;
                }
                if (score) hits.push({ entry, score });
            }
            hits.sort((a, b) => b.score - a.score);

            const summary = document.createElement('p');
            summary.textContent = hits.length
                ? `${hits.length} ${hits.length === 1 ? 'section' : 'sections'}${hits.length > 15 ? ', the best 15 shown' : ''}`
                : `Nothing found for “${searchInput.value.trim()}”.`;
            const list = document.createElement('ol');
            for (const { entry } of hits.slice(0, 15)) {
                const link = document.createElement('a');
                link.href = `#${entry.id}`;
                if (entry.chapter) link.append(Object.assign(document.createElement('small'), { textContent: entry.chapter }));
                const title = document.createElement('strong');
                title.append(marked(entry.title, words));
                const text = document.createElement('span');
                text.append(marked(excerpt(entry, words), words));
                link.append(title, text);
                const item = document.createElement('li');
                item.append(link);
                list.append(item);
            }
            resultsBox.replaceChildren(summary, ...(hits.length ? [list] : []));
            resultsBox.scrollTop = 0;
        };

        const clear = () => { searchInput.value = ''; search(); };

        searchInput.addEventListener('input', search);
        searchInput.addEventListener('keydown', e => {
            const first = resultsBox.querySelector('a');
            if (e.key === 'Escape') { clear(); searchInput.blur(); }
            if (e.key === 'Enter' && first) first.click();
            if (e.key === 'ArrowDown' && first) { e.preventDefault(); first.focus(); }
        });
        resultsBox.addEventListener('keydown', e => {
            const links = [...resultsBox.querySelectorAll('a')];
            const at = links.indexOf(document.activeElement);
            if (e.key === 'Escape') { clear(); searchInput.focus(); }
            if (e.key === 'ArrowDown' && at >= 0) { e.preventDefault(); links[Math.min(at + 1, links.length - 1)].focus(); }
            if (e.key === 'ArrowUp' && at >= 0) { e.preventDefault(); (links[at - 1] ?? searchInput).focus(); }
        });
        document.addEventListener('keydown', e => {
            if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
            if (e.target.closest('input, textarea, select, [contenteditable]')) return;
            e.preventDefault();
            searchInput.focus();
            searchInput.select();
        });
    }

    // ---- Reading progress and the way back to the top (documentation and changelog) ----
    const progress = document.querySelector('.read-progress span');
    const toTop = document.querySelector('[data-to-top]');
    if (progress && toTop) {
        const update = () => {
            const distance = root.scrollHeight - window.innerHeight;
            progress.style.transform = `scaleX(${distance > 0 ? Math.min(1, window.scrollY / distance) : 0})`;
            toTop.hidden = window.scrollY < 900;
        };
        let waiting = false;
        window.addEventListener('scroll', () => {
            if (waiting) return;
            waiting = true;
            requestAnimationFrame(() => { waiting = false; update(); });
        }, { passive: true });
        toTop.addEventListener('click', () => {
            const calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            window.scrollTo({ top: 0, behavior: calm ? 'auto' : 'smooth' });
        });
        update();
    }
})();
