(() => {
    "use strict";
    if (window.appTurnstileForms) return;
    window.appTurnstileForms = true;
    const fields = [];
    let apiLoading = false;
    let loadTimer;

    function message(state, kind, attention = false) {
        state.message.textContent = kind ? state.element.dataset[kind + "Message"] : "";
        state.message.hidden = !kind;
        state.message.classList.toggle("text-red-700", attention);
        state.message.classList.toggle("text-slate-600", !attention);
        state.retry.hidden = !attention || !state.element.dataset.sitekey;
        state.message.setAttribute("role", attention ? "alert" : "status");
    }

    function busy(state, active) {
        const button = state.submitter;
        if (active) {
            state.form.setAttribute("aria-busy", "true");
            if (button && !state.spinner) {
                state.spinner = document.createElement("span");
                state.spinner.className = "ml-2 inline-block size-4 animate-spin rounded-full border-2 border-current border-r-transparent";
                state.spinner.setAttribute("aria-hidden", "true");
                state.spinner.dataset.turnstileSpinner = "";
                button.append(state.spinner);
            }
        } else {
            state.form.removeAttribute("aria-busy");
            state.spinner?.remove();
            state.spinner = null;
            state.controls?.forEach(control => { control.element.disabled = control.disabled; });
            state.controls = null;
        }
    }

    function invalidate(state, kind) {
        state.token.value = "";
        state.issued = 0;
        state.status = kind;
        message(state, kind, true);
        if (state.waiting) state.message.focus();
        state.waiting = false;
        busy(state, false);
    }

    function render() {
        clearTimeout(loadTimer);
        apiLoading = false;
        if (!window.turnstile?.render) return;
        fields.forEach(state => {
            if (state.widget !== undefined || !state.element.dataset.sitekey) return;
            try {
                state.widget = window.turnstile.render(state.element.querySelector("[data-turnstile-widget]"), {
                    sitekey: state.element.dataset.sitekey,
                    "response-field": false,
                    "refresh-expired": "auto",
                    "refresh-timeout": "auto",
                    callback: token => {
                        state.token.value = token;
                        state.issued = Date.now();
                        state.status = "ready";
                        message(state, null);
                        state.element.querySelector("[data-turnstile-server-error]")?.remove();
                        if (state.waiting) {
                            state.waiting = false;
                            busy(state, false);
                            queueMicrotask(() => state.form.requestSubmit(state.submitter || undefined));
                        }
                    },
                    "expired-callback": () => invalidate(state, "expired"),
                    "timeout-callback": () => invalidate(state, "expired"),
                    "error-callback": code => {
                        invalidate(state, "failed");
                        console.warn("Turnstile widget error:", code);
                        return true;
                    }
                });
            } catch {
                invalidate(state, "unavailable");
            }
        });
    }

    function load() {
        if (window.turnstile?.render) { render(); return; }
        if (apiLoading) return;
        apiLoading = true;
        const failed = () => {
            clearTimeout(loadTimer);
            apiLoading = false;
            fields.forEach(state => invalidate(state, "unavailable"));
        };
        document.getElementById("app-turnstile-api")?.remove();
        const script = document.createElement("script");
        script.id = "app-turnstile-api";
        script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit&onload=appTurnstileReady";
        script.async = true;
        script.dataset.cookieconsent = "ignore";
        script.onerror = failed;
        loadTimer = setTimeout(failed, 12000);
        window.appTurnstileReady = render;
        document.head.append(script);
    }

    function refresh(state) {
        state.token.value = "";
        state.issued = 0;
        state.status = "loading";
        message(state, "loading");
        if (state.widget !== undefined && window.turnstile?.reset) window.turnstile.reset(state.widget);
        else load();
    }

    function initialize() {
        document.querySelector("[data-product-errors]")?.focus();
        document.querySelectorAll("[data-turnstile-field]").forEach(element => {
            const form = element.closest("form");
            if (!form || fields.some(state => state.element === element)) return;
            const state = { element, form, token: element.querySelector("[data-turnstile-token]"),
                message: element.querySelector("[data-turnstile-status]"), retry: element.querySelector("[data-turnstile-retry]"),
                status: "loading", issued: 0, waiting: false, posting: false };
            fields.push(state);
            state.retry.addEventListener("click", () => refresh(state));
            form.addEventListener("submit", event => {
                if (state.posting || state.waiting) { event.preventDefault(); return; }
                state.submitter = event.submitter;
                if (!state.token.value || Date.now() - state.issued >= 290000) {
                    event.preventDefault();
                    state.waiting = true;
                    busy(state, true);
                    if (!element.dataset.sitekey) { invalidate(state, "unavailable"); return; }
                    if (state.status !== "loading" || state.widget === undefined && !apiLoading) refresh(state);
                    else message(state, "loading");
                    return;
                }
                state.posting = true;
                busy(state, true);
                const disable = () => {
                    state.controls = Array.from(form.querySelectorAll("button[type=submit], input[type=submit]"))
                        .map(control => ({ element: control, disabled: control.disabled }));
                    state.controls.forEach(control => { control.element.disabled = true; });
                };
                form.addEventListener("formdata", disable, { once: true });
                setTimeout(() => {
                    if (event.defaultPrevented) {
                        form.removeEventListener("formdata", disable);
                        state.posting = false;
                        busy(state, false);
                    }
                }, 0);
            }, true);
            message(state, element.dataset.sitekey ? "loading" : "unavailable", !element.dataset.sitekey);
            element.dataset.turnstileReady = "true";
            element.querySelector("[data-turnstile-server-error]")?.focus();
        });
        if (fields.some(state => state.element.dataset.sitekey)) load();
    }
    window.addEventListener("pageshow", event => {
        if (event.persisted) fields.forEach(state => { state.posting = false; state.waiting = false; busy(state, false); refresh(state); });
    });
    if (document.querySelector("[data-turnstile-field]")) initialize();
    else document.addEventListener("DOMContentLoaded", initialize);
})();
