(() => {
    "use strict";

    const approvedForms = new WeakSet();

    function getDialogElements() {
        const dialog = document.querySelector("[data-app-dialog]");
        if (!dialog) return null;

        return {
            dialog,
            title: dialog.querySelector("[data-app-dialog-title]"),
            message: dialog.querySelector("[data-app-dialog-message]"),
            inputGroup: dialog.querySelector("[data-app-dialog-input-group]"),
            inputLabel: dialog.querySelector("[data-app-dialog-input-label]"),
            input: dialog.querySelector("[data-app-dialog-input]"),
            cancelButton: dialog.querySelector("[data-app-dialog-cancel]"),
            confirmButton: dialog.querySelector("[data-app-dialog-confirm]"),
            icons: Array.from(dialog.querySelectorAll("[data-app-dialog-icon]"))
        };
    }

    function normalizeOptions(options) {
        return typeof options === "string" ? { message: options } : (options || {});
    }

    function applyTone(elements, tone) {
        const normalizedTone = ["danger", "info"].includes(tone) ? tone : "warning";
        const dangerClasses = ["border-red-700", "bg-red-700", "hover:border-red-800", "hover:bg-red-800", "focus-visible:outline-red-700"];
        const primaryClasses = ["border-[#005190]", "bg-[#005190]", "hover:border-[#003f70]", "hover:bg-[#003f70]", "focus-visible:outline-[#005190]"];

        elements.icons.forEach(function (icon) {
            icon.hidden = icon.dataset.appDialogIcon !== normalizedTone;
        });

        dangerClasses.forEach(function (className) {
            elements.confirmButton.classList.toggle(className, normalizedTone === "danger");
        });
        primaryClasses.forEach(function (className) {
            elements.confirmButton.classList.toggle(className, normalizedTone !== "danger");
        });
    }

    function openDialog(options, expectsInput) {
        const settings = normalizeOptions(options);
        const elements = getDialogElements();

        if (!elements || typeof elements.dialog.showModal !== "function") {
            window.notify?.("This action could not be opened. Please refresh the page and try again.", {
                type: "error",
                title: "Action unavailable"
            });
            return Promise.resolve(expectsInput ? null : false);
        }

        if (elements.dialog.open) {
            elements.dialog.close("cancel");
        }

        const tone = settings.tone || "warning";
        elements.title.textContent = settings.title || (tone === "danger" ? "Confirm removal" : "Confirm action");
        elements.message.textContent = settings.message || "Are you sure you want to continue?";
        elements.cancelButton.textContent = settings.cancelLabel || "Cancel";
        elements.confirmButton.textContent = settings.confirmLabel || "Continue";
        applyTone(elements, tone);

        elements.inputGroup.hidden = !expectsInput;
        elements.inputLabel.textContent = settings.inputLabel || "Reason";
        elements.input.value = expectsInput ? (settings.value || "") : "";
        elements.input.placeholder = settings.placeholder || "";
        elements.input.required = expectsInput && settings.required !== false;
        elements.dialog.returnValue = "";

        return new Promise(function (resolve) {
            elements.dialog.addEventListener("close", function handleClose() {
                if (elements.dialog.returnValue !== "confirm") {
                    resolve(expectsInput ? null : false);
                    return;
                }

                resolve(expectsInput ? elements.input.value.trim() : true);
            }, { once: true });

            elements.dialog.showModal();
            window.requestAnimationFrame(function () {
                (expectsInput ? elements.input : elements.confirmButton).focus();
                if (expectsInput) elements.input.select();
            });
        });
    }

    window.appConfirm = function (options) {
        return openDialog(options, false);
    };

    window.appPrompt = function (options) {
        return openDialog(options, true);
    };

    document.addEventListener("submit", async function (event) {
        const form = event.target.closest("form[data-confirm-message]");
        if (!form || approvedForms.delete(form)) return;

        event.preventDefault();
        const submitter = event.submitter;
        const confirmed = await window.appConfirm({
            title: form.dataset.confirmTitle,
            message: form.dataset.confirmMessage,
            confirmLabel: form.dataset.confirmAction,
            cancelLabel: form.dataset.confirmCancel,
            tone: form.dataset.confirmTone
        });

        if (!confirmed) return;

        approvedForms.add(form);
        form.requestSubmit(submitter || undefined);
    });

    document.addEventListener("click", function (event) {
        const dialog = event.target.closest("[data-app-dialog]");
        if (dialog && event.target === dialog) dialog.close("cancel");
    });
})();
