(() => {
    "use strict";

    function initializeSpecialNumberInputs(root) {
        root.querySelectorAll("[data-special-number-input]").forEach((input) => {
            const normalizeValue = () => {
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

    const settingsCanvas = document.getElementById("examSettingsCanvas");
    if (!settingsCanvas) {
        return;
    }

    function setSettingsOpen(isOpen) {
        settingsCanvas.hidden = !isOpen;
        document.body.classList.toggle("exam-settings-open", isOpen);
        settingsCanvas.dispatchEvent(new CustomEvent(isOpen ? "exam-settings:shown" : "exam-settings:hidden"));

        if (isOpen) {
            settingsCanvas.focus();
        }
    }

    if (window.TinyMceHelpers) {
        TinyMceHelpers.configureJQueryValidationForTinyMce();
        TinyMceHelpers.wireTriggerSaveOnSubmit("#exam-settings-form");
    }

    initializeSpecialNumberInputs(document);

    const settingsForm = document.getElementById("exam-settings-form");
    let settingsSavePromise = null;

    function settingsFingerprint() {
        if (!settingsForm) return "";

        try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }

        const values = [];
        const formData = new FormData(settingsForm);
        for (const [key, value] of formData.entries()) {
            if (key === "__RequestVerificationToken" || value instanceof File) continue;
            values.push([key, String(value)]);
        }

        values.sort((left, right) => {
            const keyComparison = left[0].localeCompare(right[0]);
            return keyComparison !== 0 ? keyComparison : left[1].localeCompare(right[1]);
        });

        return JSON.stringify(values);
    }

    let savedSettingsFingerprint = settingsFingerprint();

    async function saveSettingsIfDirty() {
        if (!settingsForm) return { success: true, skipped: true };
        if (settingsSavePromise) return settingsSavePromise;

        const currentFingerprint = settingsFingerprint();
        if (currentFingerprint === savedSettingsFingerprint) {
            return { success: true, skipped: true };
        }

        settingsSavePromise = (async () => {
            const response = await fetch(settingsForm.action, {
                method: "POST",
                body: new FormData(settingsForm),
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) {
                throw new Error(result?.errorMessage || "Settings could not be saved before publishing.");
            }

            savedSettingsFingerprint = settingsFingerprint();
            return result;
        })();

        try {
            return await settingsSavePromise;
        } finally {
            settingsSavePromise = null;
        }
    }

    window.examSettingsCanvas = {
        isDirty: () => settingsFingerprint() !== savedSettingsFingerprint,
        saveIfDirty: saveSettingsIfDirty
    };

    function formatScore(value) {
        return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/\.?0+$/, "");
    }

    function updateGradeRanges(root) {
        const section = root.querySelector("[data-grade-settings-section]");
        if (!section) {
            return;
        }

        const rows = Array.from(section.querySelectorAll("[data-grade-band-row]"));
        const entries = rows.map((row, index) => {
            const input = row.querySelector("[data-grade-minimum]");
            const raw = input ? Number.parseFloat(input.value) : Number.NaN;
            return {
                index,
                row,
                minimum: Number.isFinite(raw) ? raw : null
            };
        });

        const sorted = entries
            .filter((entry) => entry.minimum !== null)
            .sort((a, b) => a.minimum - b.minimum);

        rows.forEach((row) => {
            const preview = row.querySelector("[data-grade-range-preview]");
            if (preview) {
                preview.textContent = "Not set";
            }
        });

        sorted.forEach((entry, sortedIndex) => {
            const preview = entry.row.querySelector("[data-grade-range-preview]");
            if (!preview) {
                return;
            }

            const current = entry.minimum;
            const next = sorted[sortedIndex + 1]?.minimum;
            preview.textContent = next === undefined || next === null
                ? `${formatScore(current)}+`
                : `${formatScore(current)} - ${formatScore(Math.max(current, next - 0.01))}`;
        });
    }

    const gradeToggle = document.getElementById("display-grade-on-results");
    const gradeSection = document.querySelector("[data-grade-settings-section]");
    if (gradeToggle && gradeSection) {
        const syncGradeSection = () => {
            gradeSection.classList.toggle("exam-hidden", !gradeToggle.checked);
        };

        gradeToggle.addEventListener("change", syncGradeSection);
        syncGradeSection();
    }

    document.querySelectorAll("[data-grade-minimum]").forEach((input) => {
        input.addEventListener("input", () => updateGradeRanges(document));
    });

    updateGradeRanges(document);

    document.querySelectorAll("[data-exam-settings-open]").forEach((button) => {
        button.addEventListener("click", () => setSettingsOpen(true));
    });

    document.querySelectorAll("[data-exam-settings-close]").forEach((button) => {
        button.addEventListener("click", () => setSettingsOpen(false));
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && !settingsCanvas.hidden) {
            setSettingsOpen(false);
        }
    });

    settingsCanvas.addEventListener("exam-settings:shown", () => {
        if (!window.TinyMceHelpers) {
            return;
        }

        if (TinyMceHelpers.isAvailable()) {
            tinymce.remove("#examSettingsCanvas textarea.tinymce");
        }

        TinyMceHelpers.initEditors(
            ["#examSettingsCanvas textarea.tinymce"],
            {
                height: 220,
                min_height: 160,
                plugins: "lists link paste",
                toolbar: "undo redo | bold italic underline | bullist numlist | link | removeformat",
                paste_as_text: true
            }
        );
    });

    settingsCanvas.addEventListener("exam-settings:hidden", () => {
        if (window.TinyMceHelpers?.isAvailable()) {
            tinymce.remove("#examSettingsCanvas textarea.tinymce");
        }
    });
})();
