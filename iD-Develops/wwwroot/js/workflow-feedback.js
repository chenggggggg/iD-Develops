(() => {
    "use strict";

    const progress = document.querySelector("[data-navigation-progress]");
    if (!progress) return;
    const label = progress.querySelector("[data-navigation-progress-label]");
    const origins = new Set([window.location.origin]);
    [progress.dataset.publicUrl, progress.dataset.portalUrl].forEach(value => {
        if (value) origins.add(new URL(value, window.location.href).origin);
    });
    const pendingForms = new Map();
    const scheduled = new Set();
    let navigation = null;

    function defer(action) {
        const timer = setTimeout(() => { scheduled.delete(timer); action(); }, 0);
        scheduled.add(timer);
        return timer;
    }

    function restoreAttribute(element, name, value) {
        if (value === null) element.removeAttribute(name);
        else element.setAttribute(name, value);
    }

    function resetNavigation() {
        progress.hidden = true;
        if (navigation) {
            if (navigation.main) restoreAttribute(navigation.main, "aria-busy", navigation.busy);
            navigation = null;
        }
    }

    function resetForm(form, state) {
        form.removeEventListener("formdata", state.disable);
        restoreAttribute(form, "aria-busy", state.busy);
        state.controls.forEach(control => { control.element.disabled = control.disabled; });
        state.visuals.forEach(visual => { visual.element.hidden = visual.hidden; });
        pendingForms.delete(form);
    }

    function reset() {
        scheduled.forEach(clearTimeout);
        scheduled.clear();
        resetNavigation();
        pendingForms.forEach((state, form) => resetForm(form, state));
        window.removeEventListener("beforeunload", beforeUnload);
    }

    function beforeUnload(event) {
        window.removeEventListener("beforeunload", beforeUnload);
        // Other editors may cancel navigation to protect unsaved work.
        defer(() => { if (event.defaultPrevented || (typeof event.returnValue === "string" && event.returnValue)) reset(); });
    }

    document.addEventListener("click", event => {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (!anchor || anchor.hasAttribute("download") || anchor.hasAttribute("data-no-navigation-progress")) return;
        const target = anchor.target || document.querySelector("base[target]")?.target;
        if (target && target.toLowerCase() !== "_self") return;
        const href = anchor.getAttribute("href").trim();
        if (!href || href.startsWith("#")) return;
        const url = new URL(anchor.href);
        if (!/^https?:$/.test(url.protocol) || !origins.has(url.origin)) return;
        if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && (url.hash || location.hash)) return;
        if (navigation?.url === url.href) {
            event.preventDefault();
            return;
        }
        window.addEventListener("beforeunload", beforeUnload);
        if (!navigation) {
            const main = document.querySelector("main");
            navigation = { main, busy: main?.getAttribute("aria-busy") ?? null };
        }
        navigation.url = url.href;
        navigation.main?.setAttribute("aria-busy", "true");
        const destination = (anchor.getAttribute("aria-label") || anchor.textContent).trim().replace(/\s+/g, " ").slice(0, 100);
        label.textContent = destination
            ? progress.dataset.loadingDestination.replace("{0}", destination)
            : progress.dataset.loadingLabel;
        progress.hidden = false;
        // A microtask can run between event listeners. Check cancellation after
        // all handlers, while showing progress before the browser starts leaving.
        defer(() => { if (event.defaultPrevented && navigation?.url === url.href) resetNavigation(); });
    });

    document.addEventListener("submit", event => {
        if (pendingForms.has(event.target)) event.preventDefault();
    }, true);

    window.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute("data-submit-feedback") || form.method === "dialog") return;
        if (event.defaultPrevented || pendingForms.has(form)) return;
        const target = event.submitter?.formTarget || form.target;
        if (target && target.toLowerCase() !== "_self") return;
        window.addEventListener("beforeunload", beforeUnload);
        const controls = Array.from(form.querySelectorAll("button, input[type=submit], input[type=image], input[type=reset]"))
            .map(element => ({ element, disabled: element.disabled }));
        const visuals = Array.from(form.querySelectorAll("[data-submit-idle], [data-submit-pending]"))
            .map(element => ({ element, hidden: element.hidden }));
        const state = { controls, visuals, busy: form.getAttribute("aria-busy"), disable: () => controls.forEach(control => { control.element.disabled = true; }) };
        pendingForms.set(form, state);
        form.setAttribute("aria-busy", "true");
        visuals.forEach(visual => { visual.element.hidden = visual.element.hasAttribute("data-submit-idle"); });
        // formdata fires after the submitter's name/value has been serialized.
        form.addEventListener("formdata", state.disable, { once: true });
        defer(() => { if (event.defaultPrevented && pendingForms.get(form) === state) resetForm(form, state); });
    });

    window.addEventListener("pagehide", reset);
    window.addEventListener("pageshow", reset);
})();
