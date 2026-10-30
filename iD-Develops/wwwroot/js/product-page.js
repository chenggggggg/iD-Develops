document.addEventListener("DOMContentLoaded", function () {
    document.querySelector("[data-product-errors]")?.focus();
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
    const defaultOriginalPrice = Number.parseFloat(form?.dataset.originalUnitPrice || String(defaultUnitPrice));
    const fees = ["registrationFee", "transactionFee", "serviceFee"].map(key => Number.parseFloat(form?.dataset[key] || "0"));
    const vatRate = Number.parseFloat(form?.dataset.vatPercentage || "0") / 100;
    const money = amount => Math.round((amount + Number.EPSILON) * 100) / 100;

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
        const originalUnit = selectedVariant ? Number.parseFloat(selectedVariant.dataset.originalPrice || String(unitPrice)) : defaultOriginalPrice;
        const feeTotal = fees.reduce((sum, fee) => sum + fee, 0);
        const feeVat = fees.reduce((sum, fee) => sum + money(fee * vatRate), 0);
        const total = unitPrice * quantity + feeTotal + money(unitPrice * quantity * vatRate) + feeVat;
        const originalTotal = originalUnit * quantity + feeTotal + money(originalUnit * quantity * vatRate) + feeVat;
        [["product-original-unit-price", originalUnit, unitPrice], ["product-original-total-price", originalTotal, total]].forEach(([id, original, discounted]) => {
            const element = document.getElementById(id);
            if (element) {
                element.hidden = original <= discounted;
                element.textContent = currency + " " + original.toFixed(2);
            }
        });

        if (quantityInput) {
            quantityInput.value = quantity;
        }

        if (quantityDisplay) {
            quantityDisplay.textContent = String(quantity);
        }

        unitPriceElement.textContent = currency + " " + unitPrice.toFixed(2);
        totalPriceElement.textContent = currency + " " + total.toFixed(2);
        const vatElement = document.getElementById("product-vat-price");
        if (vatElement) vatElement.textContent = currency + " " + (money(unitPrice * quantity * vatRate) + feeVat).toFixed(2);
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
