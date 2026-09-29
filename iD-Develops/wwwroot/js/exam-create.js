// wwwroot/js/pages/exam-create.js
// Page-specific logic for the Create page: UI toggle(s) + TinyMCE initialization.

(function (global) {
    "use strict";

    // ---------- CONFIG: adjust selectors here to match your Create.cshtml ----------
    const config = {
        // Button that shows secondary language fields/section.
        // Update this selector to match your button.
        secondaryToggleButtonSelector: "#add-secondary-language-btn",

        // Container/section that is shown/hidden for secondary language inputs.
        // Update this selector to match your container.
        secondarySectionSelector: "#secondary-language-section",

        // Optional: if you use "hidden" class toggling instead of display:none, set it here.
        // If null, we default to setting style.display.
        hiddenClassName: "portal-hidden",

        // Form selector if you want to be explicit
        formSelector: "form",

        // TinyMCE options (shared across primary/secondary)
        tinymceOptions: {
            height: 300,
            menubar: false,
            plugins: "lists link table code paste",
            toolbar: "undo redo | bold italic | bullist numlist | link table | code",
            branding: false,
            promotion: false
        }
    };

    // ---------- Helpers ----------
    function showElement(el) {
        if (!el) return;

        if (config.hiddenClassName && el.classList.contains(config.hiddenClassName)) {
            el.classList.remove(config.hiddenClassName);
            return;
        }

        // fallback: inline style
        el.style.display = "";
    }

    function isHidden(el) {
        if (!el) return true;

        if (config.hiddenClassName) {
            return el.classList.contains(config.hiddenClassName);
        }

        // fallback: computed style
        return getComputedStyle(el).display === "none";
    }

    function wireSecondaryToggle() {
        const btn = document.querySelector(config.secondaryToggleButtonSelector);
        const section = document.querySelector(config.secondarySectionSelector);

        // If your page doesn't have this feature, exit cleanly.
        if (!btn || !section) return;

        btn.addEventListener("click", function (e) {
            e.preventDefault();

            const wasHidden = isHidden(section);
            showElement(section);

            if (wasHidden) {
                global.ExamRichEditFields?.init(section, config.tinymceOptions);
            }
        });
    }

    function initializeSpecialNumberInputs(root) {
        root.querySelectorAll("[data-special-number-input]").forEach(function (input) {
            const normalizeValue = function () {
                const raw = input.value.trim();
                const value = Number.parseInt(raw, 10);

                if (!raw || !Number.isFinite(value) || value < 1) {
                    input.value = "";
                    return;
                }

                input.value = String(value);
            };

            input.addEventListener("input", normalizeValue);
            input.addEventListener("change", normalizeValue);
            input.addEventListener("blur", normalizeValue);

            normalizeValue();
        });
    }

    // ---------- Boot ----------
    document.addEventListener("DOMContentLoaded", function () {
        if (!global.TinyMceHelpers) {
            console.error("TinyMceHelpers not found. Ensure /js/shared/tinymce-helpers.js is loaded before this script.");
            return;
        }

        // Ensure validation works with TinyMCE-backed fields (if you use jQuery validate)
        global.TinyMceHelpers.configureJQueryValidationForTinyMce();

        global.ExamRichEditFields?.init(document, config.tinymceOptions);

        // Wire Create-page-only UI toggle
        wireSecondaryToggle();

        initializeSpecialNumberInputs(document);

        const form = document.querySelector(config.formSelector);
        if (form) {
            form.addEventListener("submit", function () {
                global.ExamRichEditFields?.flush(form);
            });
        }

    });

})(window);
