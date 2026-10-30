(() => {
    function initialize() {
        const completedProduct = document.querySelector("[data-completed-checkout-product-id]")?.dataset.completedCheckoutProductId;
        if (completedProduct) {
            try { sessionStorage.removeItem("catalog-checkout-draft:" + completedProduct); } catch { /* Storage is optional. */ }
            return;
        }
        const form = document.querySelector("form[data-checkout-product-id]");
        const productId = form?.dataset.checkoutProductId;
        if (!productId) return;
        const key = "catalog-checkout-draft:" + productId;
        const expiresAfter = 60 * 60 * 1000;
        let storage;
        try { storage = window.sessionStorage; } catch { return; }
        const fields = () => Array.from(form.elements).filter(element =>
            element.name && !element.disabled &&
            ["INPUT", "SELECT", "TEXTAREA"].includes(element.tagName) &&
            !["password", "file", "submit", "button"].includes(element.type) &&
            !["__RequestVerificationToken", "access", "cf-turnstile-response"].includes(element.name));
        function save() {
            const values = fields().map(element => ({
                name: element.name, value: element.value,
                checked: ["checkbox", "radio"].includes(element.type) ? element.checked : undefined
            }));
            try { storage.setItem(key, JSON.stringify({ expires: Date.now() + expiresAfter, values })); } catch { /* Storage is optional. */ }
        }
        try {
            const draft = JSON.parse(storage.getItem(key) || "null");
            if (draft && draft.expires > Date.now() && Array.isArray(draft.values)) {
                // Server validation retains the submitted values and may replace an expired request ID.
                if (!form.querySelector(".input-validation-error, .validation-summary-errors")) {
                    const elements = fields();
                    draft.values.forEach(value => {
                        const element = elements.find(field => field.name === value.name &&
                            (value.checked === undefined || field.value === value.value));
                        if (!element) return;
                        if (value.checked !== undefined) element.checked = value.checked;
                        else element.value = value.value;
                    });
                    form.dispatchEvent(new Event("change", { bubbles: true }));
                }
            } else storage.removeItem(key);
        } catch { /* A corrupt draft must never prevent checkout. */ }
        form.addEventListener("input", save);
        form.addEventListener("change", save);
        form.addEventListener("submit", save, { capture: true });
        form.dataset.draftReady = "true";
    }
    if (document.querySelector("form[data-checkout-product-id], [data-completed-checkout-product-id]")) initialize();
    else if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize);
    else initialize();
})();
