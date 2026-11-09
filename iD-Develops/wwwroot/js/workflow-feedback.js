(() => {
    "use strict";

    const progress = document.querySelector("[data-navigation-progress]");
    if (!progress) return;
    const label = progress.querySelector("[data-navigation-progress-label]");
    const panel = document.querySelector("[data-portal-navigation-progress]");
    const content = document.querySelector("[data-portal-page-content]");
    const portalOrigin = progress.dataset.portalUrl ? new URL(progress.dataset.portalUrl, location.href).origin : null;
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
            content.hidden = navigation.hidden;
            content.inert = navigation.inert;
            panel.hidden = navigation.panelHidden;
            if (navigation.main) navigation.main.scrollTop = navigation.scrollTop;
            window.scrollTo(navigation.scrollX, navigation.scrollY);
            const focus = navigation.focus;
            navigation = null;
            if (focus?.isConnected && !focus.closest("[hidden], [inert]")) focus.focus({ preventScroll: true });
        }
    }

    function resetForm(form, state) {
        form.removeEventListener("formdata", state.disable);
        restoreAttribute(form, "aria-busy", state.busy);
        state.controls.forEach(control => { control.element.disabled = control.disabled; });
        state.visuals.forEach(visual => { visual.element.hidden = visual.hidden; });
        if (state.button) restoreAttribute(state.button, "aria-busy", state.buttonBusy);
        state.spinner?.remove();
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
        // Only the Portal shell has a content panel to replace during navigation.
        // Public, identity, course and examination navigation stays ordinary navigation.
        if (!panel || !content || location.origin !== portalOrigin) return;
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (!anchor || anchor.hasAttribute("download") || anchor.hasAttribute("data-no-navigation-progress")) return;
        const target = anchor.target || document.querySelector("base[target]")?.target;
        if (target && target.toLowerCase() !== "_self") return;
        const href = anchor.getAttribute("href").trim();
        if (!href || href.startsWith("#")) return;
        const url = new URL(anchor.href);
        if (!/^https?:$/.test(url.protocol) || url.origin !== portalOrigin) return;
        if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && (url.hash || location.hash)) return;
        if (navigation?.url === url.href) {
            event.preventDefault();
            return;
        }
        window.addEventListener("beforeunload", beforeUnload);
        if (!navigation) {
            const main = document.querySelector("main");
            navigation = { main, busy: main?.getAttribute("aria-busy") ?? null,
                hidden: content.hidden, inert: content.inert, panelHidden: panel.hidden,
                scrollTop: main?.scrollTop || 0, scrollX: window.scrollX, scrollY: window.scrollY };
        }
        navigation.url = url.href;
        navigation.focus = document.activeElement;
        navigation.main?.setAttribute("aria-busy", "true");
        content.inert = true;
        content.hidden = true;
        panel.hidden = false;
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
        const button = event.submitter || form.querySelector("button[type=submit], input[type=submit]");
        const visuals = Array.from(button?.querySelectorAll("[data-submit-idle], [data-submit-pending]") || [])
            .map(element => ({ element, hidden: element.hidden }));
        const state = { controls, visuals, button, buttonBusy: button?.getAttribute("aria-busy") ?? null,
            busy: form.getAttribute("aria-busy"), disable: () => controls.forEach(control => { control.element.disabled = true; }) };
        pendingForms.set(form, state);
        form.setAttribute("aria-busy", "true");
        button?.setAttribute("aria-busy", "true");
        visuals.forEach(visual => { visual.element.hidden = visual.element.hasAttribute("data-submit-idle"); });
        if (button instanceof HTMLButtonElement) {
            const host = button.querySelector("[data-submit-pending]") || button;
            const hasSpinner = Array.from(host.querySelectorAll("[aria-hidden=true]")).some(element =>
                element.classList.contains("animate-spin") || element.classList.contains("motion-safe:animate-spin"));
            if (!hasSpinner) {
                state.spinner = document.createElement("span");
                state.spinner.dataset.submitSpinner = "";
                state.spinner.setAttribute("aria-hidden", "true");
                state.spinner.className = "inline-block size-4 shrink-0 rounded-full border-2 border-current border-r-transparent align-middle motion-safe:animate-spin";
                host.prepend(state.spinner);
            }
        }
        // formdata fires after the submitter's name/value has been serialized.
        form.addEventListener("formdata", state.disable, { once: true });
        defer(() => { if (event.defaultPrevented && pendingForms.get(form) === state) resetForm(form, state); });
    });

    window.addEventListener("pagehide", reset);
    window.addEventListener("pageshow", reset);
    progress.dataset.workflowFeedbackReady = "true";
})();
