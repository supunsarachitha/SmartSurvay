// SmartSurvey client helpers (kept deliberately small: all business logic runs on the server).
window.SmartSurvey = (function () {
    const THEME_KEY = "ss-theme";

    function preferredTheme() {
        try {
            const stored = localStorage.getItem(THEME_KEY);
            if (stored === "light" || stored === "dark") return stored;
        } catch { /* storage unavailable */ }
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function applyTheme(theme) {
        const value = theme || preferredTheme();
        document.documentElement.setAttribute("data-bs-theme", value);
        document.querySelectorAll("[data-theme-icon]").forEach(el => {
            el.className = value === "dark" ? "bi bi-sun" : "bi bi-moon-stars";
        });
        return value;
    }

    function toggleTheme() {
        const next = (document.documentElement.getAttribute("data-bs-theme") === "dark") ? "light" : "dark";
        try { localStorage.setItem(THEME_KEY, next); } catch { /* ignore */ }
        return applyTheme(next);
    }

    // Mobile sidebar (admin layout) — works on statically rendered layouts.
    function toggleSidebar(force) {
        const sidebar = document.querySelector(".app-sidebar");
        const backdrop = document.querySelector(".sidebar-backdrop");
        if (!sidebar) return;
        const show = typeof force === "boolean" ? force : !sidebar.classList.contains("show");
        sidebar.classList.toggle("show", show);
        if (backdrop) backdrop.classList.toggle("show", show);
    }

    // Downloads a file streamed from .NET (DotNetStreamReference) or a byte array.
    async function downloadFile(fileName, contentType, streamRef) {
        const buffer = streamRef.arrayBuffer ? await streamRef.arrayBuffer() : streamRef;
        const blob = new Blob([buffer], { type: contentType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = fileName || "download";
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    async function copyText(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            const ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "fixed";
            ta.style.opacity = "0";
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand("copy");
            ta.remove();
            return ok;
        }
    }

    function getTimeZone() {
        try { return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC"; } catch { return "UTC"; }
    }

    function getUserAgent() { return navigator.userAgent || null; }

    function print() { window.print(); }

    // Bot protection: solves the survey's proof-of-work challenge in a background thread (js/pow-worker.js).
    // Resolves with the number, or null when it can't be solved (the server then asks the person to reload).
    function solveChallenge(salt, challenge, max) {
        return new Promise(resolve => {
            let worker;
            try { worker = new Worker("js/pow-worker.js"); } catch { resolve(null); return; }
            const done = value => { clearTimeout(timer); worker.terminate(); resolve(value); };
            const timer = setTimeout(() => done(null), 60000);
            worker.onmessage = e => done(typeof e.data === "number" ? e.data : null);
            worker.onerror = () => done(null);
            worker.postMessage({ salt, challenge, max });
        });
    }

    function scrollToTop() { window.scrollTo({ top: 0, behavior: "smooth" }); }

    function scrollIntoView(selector) {
        const el = document.querySelector(selector);
        if (el) el.scrollIntoView({ behavior: "smooth", block: "center" });
    }

    function focus(selector) {
        const el = document.querySelector(selector);
        if (el) el.focus();
    }

    // Close the mobile sidebar and re-apply theme after Blazor enhanced navigation re-renders the DOM.
    function onPageLoaded() {
        applyTheme();
        toggleSidebar(false);
    }

    document.addEventListener("click", e => {
        if (e.target.closest("[data-action='toggle-theme']")) { e.preventDefault(); toggleTheme(); }
        if (e.target.closest("[data-action='toggle-sidebar']")) { e.preventDefault(); toggleSidebar(); }
        if (e.target.closest(".sidebar-backdrop")) { toggleSidebar(false); }
    });

    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener("enhancedload", onPageLoaded);
    }
    document.addEventListener("DOMContentLoaded", () => applyTheme());

    // Guard: enhanced navigation may synchronise <html> attributes with the server markup and drop
    // the theme attribute; restore the user's choice immediately when that happens.
    new MutationObserver(() => {
        const wanted = preferredTheme();
        if (document.documentElement.getAttribute("data-bs-theme") !== wanted) applyTheme(wanted);
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });

    return { applyTheme, toggleTheme, toggleSidebar, downloadFile, copyText, getTimeZone, getUserAgent, print, solveChallenge, scrollToTop, scrollIntoView, focus };
})();
