document.addEventListener("DOMContentLoaded", function () {
    function activateProductTab(tab) {
        const targetId = tab?.dataset.productTabTarget;
        if (!targetId) {
            return;
        }

        const tabList = tab.closest('[role="tablist"]');
        const tabs = tabList?.querySelectorAll("[data-product-tab]") || [];

        tabs.forEach(function (button) {
            const isActive = button === tab;
            button.classList.toggle("is-active", isActive);
            button.setAttribute("aria-selected", isActive ? "true" : "false");
            button.tabIndex = isActive ? 0 : -1;
        });

        document.querySelectorAll("[data-product-tab-panel]").forEach(function (panel) {
            panel.hidden = panel.id !== targetId;
        });
    }

    document.querySelectorAll("[data-product-tab]").forEach(function (tab) {
        tab.addEventListener("click", function () {
            activateProductTab(tab);
        });
    });

    const form = document.getElementById("product-submit-form");
    const quantityInput = document.getElementById("orderQuantity");
    const quantityDisplay = document.getElementById("product-quantity-display");
    const unitPriceElement = document.getElementById("product-unit-price");
    const totalPriceElement = document.getElementById("product-total-price");
    const variantInputs = document.querySelectorAll('input[name="selectedVariantId"]');
    const participantCount = document.getElementById("participantCount");
    const defaultUnitPrice = Number.parseFloat(form?.dataset.defaultUnitPrice || "0");
    const defaultCurrency = form?.dataset.defaultCurrency || "EUR";

    function clampQuantity(value) {
        if (!quantityInput) {
            return 1;
        }

        const min = Number(quantityInput.dataset.min || "1");
        const max = Number(quantityInput.dataset.max || String(min));
        const parsed = Number.parseInt(value, 10);

        if (!Number.isFinite(parsed)) {
            return min;
        }

        return Math.min(Math.max(parsed, min), max);
    }

    function getSelectedVariant() {
        return Array.from(variantInputs).find(function (input) {
            return input.checked;
        }) || null;
    }

    function updateTotals() {
        if (!totalPriceElement || !unitPriceElement) {
            return;
        }

        const selectedVariant = getSelectedVariant();
        const unitPrice = selectedVariant
            ? Number.parseFloat(selectedVariant.dataset.price || "0")
            : defaultUnitPrice;
        const currency = selectedVariant
            ? (selectedVariant.dataset.currency || defaultCurrency)
            : defaultCurrency;
        const quantity = quantityInput ? clampQuantity(quantityInput.value) : 1;
        const total = unitPrice * quantity;

        if (quantityInput) {
            quantityInput.value = quantity;
        }

        if (quantityDisplay) {
            quantityDisplay.textContent = String(quantity);
        }

        unitPriceElement.textContent = currency + " " + unitPrice.toFixed(2);
        totalPriceElement.textContent = currency + " " + total.toFixed(2);
    }

    if (quantityInput) {
        document.querySelectorAll("[data-quantity-action]").forEach(function (button) {
            button.addEventListener("click", function () {
                const direction = button.getAttribute("data-quantity-action");
                const current = clampQuantity(quantityInput.value);
                quantityInput.value = direction === "increase" ? current + 1 : current - 1;
                updateTotals();
            });
        });

        quantityInput.addEventListener("input", updateTotals);
        quantityInput.addEventListener("blur", updateTotals);
    }

    variantInputs.forEach(function (input) {
        input.addEventListener("change", updateTotals);
    });

    if (participantCount) {
        participantCount.addEventListener("change", function () {
            participantCount.form?.requestSubmit();
        });
    }

    updateTotals();
});
