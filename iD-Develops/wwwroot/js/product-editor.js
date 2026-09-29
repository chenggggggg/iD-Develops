document.addEventListener("DOMContentLoaded", function () {
    const form = document.getElementById("product-edit-form");
    const currencySelect = document.getElementById("Input_Currency");
    const basePriceSymbol = document.getElementById("base-price-symbol");
    const newVariantCurrencySelect = document.getElementById("NewVariant_Currency");
    const newVariantCurrencySymbol = document.getElementById("new-variant-currency-symbol");
    const productTypeSelect = document.getElementById("Input_ProductType");
    const allowsMultipleParticipantsCheckbox = document.getElementById("Input_AllowsMultipleParticipants");
    const enableQuantityCheckbox = document.getElementById("Input_EnableQuantity");
    const featuredCheckbox = document.getElementById("Input_IsFeatured");
    const hideFromProductsCheckbox = document.getElementById("Input_HideFromProductsPage");
    const requireAccessTokenCheckbox = document.getElementById("Input_RequireAccessToken");
    let skipUnloadWarning = false;
    let initialSnapshot = "";

    function updateCurrencySymbol() {
        if (!currencySelect || !basePriceSymbol) {
            return;
        }

        const map = { EUR: "EUR", USD: "USD", GBP: "GBP" };
        basePriceSymbol.textContent = map[currencySelect.value] || currencySelect.value || "EUR";
    }

    function updateVariantCurrencySymbols() {
        if (newVariantCurrencySelect && newVariantCurrencySymbol) {
            newVariantCurrencySymbol.textContent = newVariantCurrencySelect.value || "EUR";
        }

        document.querySelectorAll("form").forEach(function (formElement) {
            const currentCurrencySelect = formElement.querySelector(".variant-currency-select");
            const priceSymbol = formElement.querySelector(".input-group-text");
            if (!currentCurrencySelect || !priceSymbol) {
                return;
            }

            priceSymbol.textContent = currentCurrencySelect.value || "EUR";
        });
    }

    function takeSnapshot() {
        if (!form) {
            return "";
        }

        if (window.tinymce) {
            window.tinymce.triggerSave();
        }

        return new URLSearchParams(new FormData(form)).toString();
    }

    function formatPriceInput(input) {
        if (!input || !input.value.trim()) {
            return;
        }

        const raw = input.value.trim().replace(/[^\d,.-]/g, "");
        const lastComma = raw.lastIndexOf(",");
        const lastDot = raw.lastIndexOf(".");
        let normalized = raw;

        if (lastComma > lastDot) {
            normalized = normalized.replace(/\./g, "").replace(",", ".");
        } else if (lastDot > lastComma) {
            normalized = normalized.replace(/,/g, "");
        }

        const parsed = Number(normalized);
        if (!Number.isFinite(parsed)) {
            return;
        }

        const formatter = new Intl.NumberFormat(undefined, {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });

        input.value = formatter.format(parsed);
    }

    function setSectionVisibility(selector, shouldShow) {
        document.querySelectorAll(selector).forEach(function (element) {
            element.classList.toggle("product-hidden", !shouldShow);
            element.querySelectorAll("input, select, textarea").forEach(function (input) {
                if (input.name === "Input.ProductType" || input.name === "Input.WorkflowType") {
                    return;
                }

                if (input.closest(".tinymce")) {
                    return;
                }

                input.disabled = !shouldShow;
            });
        });
    }

    function updateQuantityVisibility() {
        const quantityRangeSettings = document.querySelectorAll(".quantity-range-settings");
        const quantityDisabledNote = document.querySelector(".quantity-disabled-note");
        const allowsMultipleParticipants = allowsMultipleParticipantsCheckbox?.checked === true;
        const quantityEnabled = enableQuantityCheckbox?.checked === true;

        if (enableQuantityCheckbox) {
            enableQuantityCheckbox.disabled = allowsMultipleParticipants;
            if (allowsMultipleParticipants) {
                enableQuantityCheckbox.checked = false;
            }
        }

        quantityRangeSettings.forEach(function (element) {
            element.classList.toggle("product-hidden", !quantityEnabled || allowsMultipleParticipants);
            element.querySelectorAll("input, select, textarea").forEach(function (input) {
                input.disabled = !quantityEnabled || allowsMultipleParticipants;
            });
        });

        if (quantityDisabledNote) {
            quantityDisabledNote.classList.toggle("product-hidden", !allowsMultipleParticipants);
        }
    }

    function updateParticipantVisibility(shouldShowParticipantSettings) {
        const participantCountSections = document.querySelectorAll(".workflow-participants-count");
        const allowsMultipleParticipants = allowsMultipleParticipantsCheckbox?.checked === true;

        participantCountSections.forEach(function (element) {
            element.classList.toggle("product-hidden", !shouldShowParticipantSettings || !allowsMultipleParticipants);
            element.querySelectorAll("input, select, textarea").forEach(function (input) {
                input.disabled = !shouldShowParticipantSettings || !allowsMultipleParticipants;
            });
        });
    }

    function updateProductsOverviewVisibility() {
        if (!featuredCheckbox || !hideFromProductsCheckbox) {
            return;
        }

        if (hideFromProductsCheckbox.checked) {
            featuredCheckbox.checked = false;
        }

        if (featuredCheckbox.checked) {
            hideFromProductsCheckbox.checked = false;
        }

        featuredCheckbox.disabled = hideFromProductsCheckbox.checked;
        hideFromProductsCheckbox.disabled = featuredCheckbox.checked;
    }

    function updateSecureInviteVisibility() {
        const shouldShow = requireAccessTokenCheckbox?.checked === true;
        setSectionVisibility(".secure-invite-settings", shouldShow);
    }

    function updateFieldEditorVisibility(editor) {
        if (!editor) {
            return;
        }

        const typeSelect = editor.querySelector(".product-field-type");
        const placeholderGroup = editor.querySelector(".product-field-placeholder-group");
        const placeholderLabel = editor.querySelector(".product-field-placeholder-label");
        const placeholderInput = placeholderGroup?.querySelector("input");
        const optionsGroup = editor.querySelector(".product-field-options-group");
        const optionsLabel = editor.querySelector(".product-field-options-label");
        const optionsHelp = editor.querySelector(".product-field-options-help");
        const optionsTextarea = optionsGroup?.querySelector("textarea");

        if (!typeSelect) {
            return;
        }

        const selectedType = typeSelect.value;
        const isSelect = selectedType === "Select" || selectedType === "6";
        const isCheckbox = selectedType === "Checkbox" || selectedType === "5";
        const isTextarea = selectedType === "Textarea" || selectedType === "4";

        if (placeholderLabel) {
            placeholderLabel.textContent = isCheckbox
                ? "Checkbox label text"
                : (isTextarea ? "Textarea placeholder" : "Placeholder");
        }

        if (placeholderInput) {
            placeholderInput.placeholder = isCheckbox
                ? "For example: I agree to the terms"
                : isTextarea
                    ? "For example: Tell us about your goals"
                    : isSelect
                        ? "Optional prompt shown above the dropdown"
                        : "For example: Enter your answer";
        }

        if (optionsGroup) {
            optionsGroup.classList.toggle("product-hidden", !isSelect);
        }

        if (optionsTextarea) {
            optionsTextarea.disabled = !isSelect;
        }

        if (optionsLabel) {
            optionsLabel.textContent = "Dropdown options";
        }

        if (optionsHelp) {
            optionsHelp.textContent = "Only needed for dropdown fields. Use one option per line.";
        }
    }

    function updateJourneyVisibility() {
        if (!productTypeSelect) {
            return;
        }

        const collect = document.getElementById("Input_CollectCustomerDetails")?.checked;
        const booking = document.getElementById("Input_SendToBookingPage")?.checked;
        const payment = document.getElementById("Input_TakePaymentNow")?.checked;
        let workflow = null;

        if (collect && !booking && !payment) workflow = "1";
        if (collect && booking && !payment) workflow = "2";
        if (collect && !booking && payment) workflow = "3";
        if (!collect && booking && !payment) workflow = "4";

        const productType = productTypeSelect.value;
        const usesExternalBooking = workflow === "2" || workflow === "4";
        const usesForm = workflow !== "4";
        const usesPayment = workflow === "3";
        const isDigital = productType === "3";
        const canUseParticipantSettings = usesForm && !usesPayment;

        setSectionVisibility(".workflow-external-only", usesExternalBooking);
        setSectionVisibility(".workflow-form-based", usesForm);
        setSectionVisibility(".workflow-participants", canUseParticipantSettings);
        setSectionVisibility(".payment-settings", usesPayment);
        setSectionVisibility(".post-purchase-settings", isDigital);
        setSectionVisibility(".product-digital-only", isDigital);

        const formCheckbox = document.getElementById("Input_CollectCustomerDetails");
        const bookingCheckbox = document.getElementById("Input_SendToBookingPage");
        const paymentCheckbox = document.getElementById("Input_TakePaymentNow");

        if (formCheckbox) {
            formCheckbox.disabled = productType === "1";
            if (productType === "1") {
                formCheckbox.checked = true;
            }
        }

        if (bookingCheckbox) {
            bookingCheckbox.disabled = productType === "1" || productType === "2" || productType === "3";
            if (productType === "2") {
                bookingCheckbox.checked = true;
            }
        }

        if (paymentCheckbox) {
            paymentCheckbox.disabled = productType === "1" || productType === "2";
        }

        updateParticipantVisibility(canUseParticipantSettings);
        updateQuantityVisibility();
    }

    function applyDefaultJourneyForProductType() {
        if (!productTypeSelect) {
            return;
        }

        const defaults = {
            "1": { collect: true, booking: false, payment: false },
            "2": { collect: true, booking: true, payment: false },
            "3": { collect: true, booking: false, payment: true },
            "4": { collect: true, booking: false, payment: true }
        };

        const selected = defaults[productTypeSelect.value];
        if (!selected) {
            return;
        }

        document.getElementById("Input_CollectCustomerDetails").checked = selected.collect;
        document.getElementById("Input_SendToBookingPage").checked = selected.booking;
        document.getElementById("Input_TakePaymentNow").checked = selected.payment;

        updateJourneyVisibility();
    }

    if (window.TinyMceHelpers) {
        TinyMceHelpers.configureJQueryValidationForTinyMce();
        TinyMceHelpers.initEditors(["#summary-editor-edit", "#full-description-editor-edit"], {
            height: 220,
            plugins: "lists link paste",
            toolbar: "bold italic underline | bullist numlist | link | removeformat"
        });
        TinyMceHelpers.wireTriggerSaveOnSubmit("#product-edit-form");
    }

    updateCurrencySymbol();
    initialSnapshot = takeSnapshot();

    if (currencySelect) {
        currencySelect.addEventListener("change", updateCurrencySymbol);
    }

    if (newVariantCurrencySelect) {
        newVariantCurrencySelect.addEventListener("change", updateVariantCurrencySymbols);
    }

    document.querySelectorAll(".variant-currency-select").forEach(function (element) {
        element.addEventListener("change", updateVariantCurrencySymbols);
    });

    if (productTypeSelect) {
        productTypeSelect.addEventListener("change", applyDefaultJourneyForProductType);
    }

    ["Input_CollectCustomerDetails", "Input_SendToBookingPage", "Input_TakePaymentNow"].forEach(function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.addEventListener("change", updateJourneyVisibility);
        }
    });

    [allowsMultipleParticipantsCheckbox, enableQuantityCheckbox].forEach(function (element) {
        if (element) {
            element.addEventListener("change", function () {
                updateParticipantVisibility(!document.querySelector(".workflow-participants")?.classList.contains("product-hidden"));
                updateQuantityVisibility();
            });
        }
    });

    [featuredCheckbox, hideFromProductsCheckbox].forEach(function (element) {
        if (element) {
            element.addEventListener("change", updateProductsOverviewVisibility);
        }
    });

    if (requireAccessTokenCheckbox) {
        requireAccessTokenCheckbox.addEventListener("change", updateSecureInviteVisibility);
    }

    document.querySelectorAll("[data-confirm-submit]").forEach(function (button) {
        button.addEventListener("click", function (event) {
            const message = button.getAttribute("data-confirm-submit") || "Are you sure?";
            if (!window.confirm(message)) {
                event.preventDefault();
            }
        });
    });

    document.querySelectorAll(".product-field-editor").forEach(function (editor) {
        const typeSelect = editor.querySelector(".product-field-type");
        if (typeSelect) {
            typeSelect.addEventListener("change", function () {
                updateFieldEditorVisibility(editor);
            });
        }

        updateFieldEditorVisibility(editor);
    });

    const basePriceInput = document.getElementById("Input_BasePriceText");
    if (basePriceInput) {
        basePriceInput.addEventListener("blur", function () {
            formatPriceInput(basePriceInput);
        });
    }

    document.querySelectorAll(".variant-price-input").forEach(function (element) {
        element.addEventListener("blur", function () {
            formatPriceInput(element);
        });
    });

    if (form) {
        form.addEventListener("submit", function () {
            document.querySelectorAll("#product-edit-form .product-hidden input, #product-edit-form .product-hidden select, #product-edit-form .product-hidden textarea").forEach(function (input) {
                input.disabled = true;
            });
            skipUnloadWarning = true;
        });
    }

    updateJourneyVisibility();
    updateQuantityVisibility();
    updateProductsOverviewVisibility();
    updateSecureInviteVisibility();
    updateVariantCurrencySymbols();

    window.addEventListener("beforeunload", function (event) {
        if (skipUnloadWarning) {
            return;
        }

        if (takeSnapshot() === initialSnapshot) {
            return;
        }

        event.preventDefault();
        event.returnValue = "";
    });
});
