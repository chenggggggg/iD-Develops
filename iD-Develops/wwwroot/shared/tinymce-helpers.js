// wwwroot/js/shared/tinymce-helpers.js
// Shared, page-agnostic helpers for TinyMCE lifecycle and form submission.

(function (global) {
    "use strict";

    const TinyMceHelpers = {

        /**
         * Returns true if TinyMCE is present on the page.
         */
        isAvailable: function () {
            return typeof global.tinymce !== "undefined" && !!global.tinymce;
        },

        /**
         * Returns true if an element exists for a given selector.
         */
        elementExists: function (selector) {
            return !!document.querySelector(selector);
        },

        /**
         * Filters selectors down to those that exist in the DOM.
         */
        getExistingSelectors: function (selectors) {
            if (!Array.isArray(selectors)) return [];
            return selectors.filter(
                s => typeof s === "string" &&
                    s.trim().length > 0 &&
                    this.elementExists(s)
            );
        },

        /**
         * Initializes TinyMCE for the given selectors.
         *
         * Supports two patterns:
         *  1) initEditors([".tinymce"], { ...overrides })
         *  2) initEditors([".scenario", ".question"], {
         *        selectorOptions: {
         *          ".scenario": { height: 220, toolbar: "..." },
         *          ".question":  { height: 140, toolbar: "..." }
         *        }
         *     })
         */
        initEditors: function (selectors, options) {
            if (!this.isAvailable()) {
                console.error(
                    "TinyMCE is not loaded. Ensure /lib/tinymce/tinymce.min.js is referenced before this script."
                );
                return;
            }

            const existing = this.getExistingSelectors(selectors);
            if (!existing.length) return;

            const opts = options || {};
            const selectorOptions = opts.selectorOptions || null;

            // Base compact defaults (can be overridden per page / per selector)
            const baseConfig = {
                menubar: false,
                statusbar: false,
                branding: false,
                promotion: false,
                license_key: "gpl",

                // A compact, exam-authoring-friendly set
                height: 180,
                min_height: 140,
                plugins: "lists link paste",
                toolbar: "bold italic underline | bullist numlist | link | removeformat",
                toolbar_mode: "sliding",

                // Make the content feel like a form field, not a CMS editor
                content_style: [
                    "body { font-size: 14px; line-height: 1.5; }",
                    "p { margin: 0 0 0.75em; }"
                ].join(" "),

                // Keep paste sane
                paste_as_text: true
            };

            // If the caller provided selectorOptions, initialize each selector separately
            // so each can have different heights/toolbar without affecting others.
            if (selectorOptions && typeof selectorOptions === "object") {
                existing.forEach(sel => {
                    const perSel = selectorOptions[sel] || {};
                    global.tinymce.init({
                        ...baseConfig,
                        selector: sel,
                        ...this._stripInternalOptions(opts),
                        ...perSel
                    });
                });
                return;
            }

            // Default behavior: one init for all selectors
            global.tinymce.init({
                ...baseConfig,
                selector: existing.join(","),
                ...this._stripInternalOptions(opts)
            });
        },

        /**
         * Removes internal helper-only keys from options so they don't get passed into TinyMCE.
         */
        _stripInternalOptions: function (opts) {
            const clone = { ...(opts || {}) };
            delete clone.selectorOptions;
            return clone;
        },

        /**
         * Destroys TinyMCE instances for specific textarea IDs.
         * Pass IDs without '#', e.g. ["introduction-editor-primary"].
         */
        removeEditorsById: function (ids) {
            if (!this.isAvailable() || !Array.isArray(ids)) return;

            ids.forEach(id => {
                const ed = global.tinymce.get(id);
                if (ed) ed.remove();
            });
        },

        /**
         * Ensures jQuery Validate does not ignore TinyMCE-backed textareas.
         */
        configureJQueryValidationForTinyMce: function () {
            if (!global.$ || !$.validator) return;

            $.validator.setDefaults({
                ignore: ":hidden:not(textarea)"
            });
        },

        /**
         * Hooks a form submit so TinyMCE pushes content back into the textareas.
         */
        wireTriggerSaveOnSubmit: function (formSelector) {
            const sel = formSelector || "form";
            const form = document.querySelector(sel);
            if (!form) return;

            form.addEventListener("submit", function () {
                if (TinyMceHelpers.isAvailable()) {
                    global.tinymce.triggerSave();
                }
            });
        }
    };

    // Expose globally
    global.TinyMceHelpers = TinyMceHelpers;

})(window);
