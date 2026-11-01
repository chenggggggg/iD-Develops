(() => {
    const flow = document.querySelector("[data-signup-flow]");
    if (!flow) return;
    const form = flow.querySelector("form");
    const panels = Array.from(flow.querySelectorAll("[data-signup-step]"));
    const personal = panels.find(panel => panel.dataset.signupStep === "1");
    let step = Number(flow.dataset.initialStep);
    let posting = false;

    function show(next, focus = true) {
        step = next;
        panels.forEach(panel => { panel.hidden = Number(panel.dataset.signupStep) !== next; });
        form.hidden = next === 0;
        flow.querySelectorAll("[data-signup-progress]").forEach(item => {
            const active = Number(item.dataset.signupProgress) === next;
            if (active) item.setAttribute("aria-current", "step");
            else item.removeAttribute("aria-current");
            item.classList.toggle("border-current", active);
            item.classList.toggle("font-semibold", active);
            item.classList.toggle("border-slate-200", !active);
        });
        if (next === 2) document.dispatchEvent(new Event("turnstile:initialize"));
        if (focus) panels[next].querySelector("h1").focus();
    }

    function validatePersonal() {
        const inputs = Array.from(personal.querySelectorAll("input"));
        inputs.forEach(input => { input.value = input.value.trim(); });
        const invalid = inputs.find(input => !input.checkValidity());
        if (invalid) { show(1); invalid.reportValidity(); return false; }
        return true;
    }

    function validateQuestions() {
        const panel = panels[2];
        panel.querySelectorAll("[data-required-question=true]").forEach(row => {
            const inputs = Array.from(row.querySelectorAll("input, select, textarea"));
            if (!inputs.length) return;
            const answered = inputs.some(input => input.type === "checkbox" || input.type === "radio"
                ? input.checked : input.type !== "hidden" && input.value.trim());
            inputs[0].setCustomValidity(answered ? "" : row.dataset.requiredMessage);
        });
        const invalid = Array.from(panel.querySelectorAll("input, select, textarea")).find(input => !input.checkValidity());
        if (invalid) { show(2); invalid.reportValidity(); return false; }
        return true;
    }

    flow.querySelectorAll("[data-signup-go]").forEach(button => button.addEventListener("click", () => {
        if (form.getAttribute("aria-busy") !== "true") show(Number(button.dataset.signupGo));
    }));
    flow.querySelector("[data-signup-continue]").addEventListener("click", () => {
        if (validatePersonal()) show(2);
    });
    form.addEventListener("input", event => {
        const row = event.target.closest("[data-required-question]");
        row?.querySelector("input, select, textarea")?.setCustomValidity("");
    });
    // Validate before the Turnstile manager consumes a single-use token.
    window.addEventListener("submit", event => {
        if (event.target !== form) return;
        if (posting || !validatePersonal() || step !== 2 || !validateQuestions()) {
            event.preventDefault();
            event.stopImmediatePropagation();
            if (!posting && step === 1 && validatePersonal()) show(2);
        }
    }, true);
    form.addEventListener("submit", event => {
        if (event.defaultPrevented || form.querySelector("[data-turnstile-field]")) return;
        posting = true;
        form.setAttribute("aria-busy", "true");
        form.querySelector("[data-signup-spinner]").classList.remove("hidden");
        form.addEventListener("formdata", () => {
            form.querySelectorAll("button").forEach(button => { button.disabled = true; });
        }, { once: true });
    });
    window.addEventListener("pageshow", event => {
        if (!event.persisted) return;
        posting = false;
        form.removeAttribute("aria-busy");
        form.querySelector("[data-signup-spinner]").classList.add("hidden");
        form.querySelectorAll("button").forEach(button => { button.disabled = false; });
    });
    show(step, false);
})();
