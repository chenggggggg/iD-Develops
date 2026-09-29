(function () {
    "use strict";

    function formatScore(value) {
        return Number.isInteger(value) ? String(value) : value.toFixed(1).replace(/\.?0+$/, "");
    }

    function getEmptyRangePreview(maxScore) {
        const upperBound = maxScore !== null ? formatScore(maxScore) : "10";
        return `Example: 0 - ${upperBound}`;
    }

    function hasAtMostOneDecimal(value) {
        return Math.abs(value * 10 - Math.round(value * 10)) < 0.0001;
    }

    function getGradeSection(root) {
        return root.querySelector("[data-grade-settings-section]");
    }

    function getGradeRows(root) {
        const section = getGradeSection(root);
        return section ? Array.from(section.querySelectorAll("[data-grade-band-row]")) : [];
    }

    function getGradeMaximumScore(root) {
        const section = getGradeSection(root);
        const raw = section ? Number.parseFloat(section.getAttribute("data-grade-max-score") || "") : Number.NaN;
        return Number.isFinite(raw) ? raw : null;
    }

    function getValidationMessageElement(input) {
        if (!input || !input.name) {
            return null;
        }

        return document.querySelector(`[data-valmsg-for="${input.name}"]`);
    }

    function clearFormSummaries(root) {
        const topSummary = root.querySelector("[data-settings-client-summary]");
        const footerSummary = root.querySelector("[data-settings-footer-summary]");

        if (topSummary) {
            topSummary.textContent = "";
            topSummary.classList.add("exam-hidden");
        }

        if (footerSummary) {
            footerSummary.textContent = "";
            footerSummary.classList.add("exam-hidden");
        }
    }

    function setFormSummary(root, message) {
        const topSummary = root.querySelector("[data-settings-client-summary]");
        const footerSummary = root.querySelector("[data-settings-footer-summary]");

        if (topSummary) {
            topSummary.textContent = message;
            topSummary.classList.remove("exam-hidden");
        }

        if (footerSummary) {
            footerSummary.textContent = message;
            footerSummary.classList.remove("exam-hidden");
        }
    }

    function clearGradeValidation(root) {
        clearFormSummaries(root);

        const summary = root.querySelector("[data-grade-client-summary]");
        if (summary) {
            summary.textContent = "";
            summary.classList.add("exam-hidden");
        }

        getGradeRows(root).forEach(function (row) {
            row.classList.remove("grade-settings-row-error");
            row.querySelectorAll("input").forEach(function (input) {
                input.setCustomValidity("");
                input.classList.remove("input-validation-error");
                input.classList.remove("is-invalid");
                input.removeAttribute("aria-invalid");
            });

            row.querySelectorAll("[data-valmsg-for]").forEach(function (message) {
                message.textContent = "";
                message.classList.remove("field-validation-error");
                message.classList.add("field-validation-valid");
            });
        });

        const toggle = document.getElementById("settings-page-display-grade-on-results");
        const toggleMessage = toggle ? getValidationMessageElement(toggle) : null;
        if (toggle && toggleMessage) {
            toggle.setCustomValidity("");
            toggle.classList.remove("is-invalid");
            toggle.removeAttribute("aria-invalid");
            toggleMessage.textContent = "";
            toggleMessage.classList.remove("field-validation-error");
            toggleMessage.classList.add("field-validation-valid");
        }
    }

    function setClientSummary(root, message) {
        const summary = root.querySelector("[data-grade-client-summary]");
        if (!summary || !message) {
            return;
        }

        summary.textContent = message;
        summary.classList.remove("exam-hidden");
    }

    function setInputValidation(input, message) {
        if (!input || !message) {
            return;
        }

        input.setCustomValidity(message);
        input.classList.add("input-validation-error");
        input.classList.add("is-invalid");
        input.setAttribute("aria-invalid", "true");

        const row = input.closest("[data-grade-band-row]");
        if (row) {
            row.classList.add("grade-settings-row-error");
        }

        const validationMessage = getValidationMessageElement(input);
        if (validationMessage) {
            validationMessage.textContent = message;
            validationMessage.classList.remove("field-validation-valid");
            validationMessage.classList.add("field-validation-error");
        }
    }

    function setToggleValidation(message) {
        const toggle = document.getElementById("settings-page-display-grade-on-results");
        if (!toggle || !message) {
            return;
        }

        toggle.setCustomValidity(message);
        toggle.classList.add("is-invalid");
        toggle.setAttribute("aria-invalid", "true");
        const validationMessage = getValidationMessageElement(toggle);
        if (validationMessage) {
            validationMessage.textContent = message;
            validationMessage.classList.remove("field-validation-valid");
            validationMessage.classList.add("field-validation-error");
        }
    }

    function validateGradeSection(root) {
        clearGradeValidation(root);

        const toggle = document.getElementById("settings-page-display-grade-on-results");
        if (!toggle || !toggle.checked) {
            return true;
        }

        const maxScore = getGradeMaximumScore(root);
        if (maxScore === null || maxScore <= 0) {
            setToggleValidation("Add at least one question with points before enabling custom grading.");
            return false;
        }

        const rows = getGradeRows(root);
        const completedRows = [];
        const usedGradeNames = new Map();
        let isValid = true;

        rows.forEach(function (row, index) {
            const scoreInput = row.querySelector("[data-grade-maximum]");
            const labelInput = row.querySelector('input[name$=".LabelPrimary"]');
            const scoreRaw = scoreInput ? scoreInput.value.trim() : "";
            const labelRaw = labelInput ? labelInput.value.trim() : "";
            const hasScore = scoreRaw.length > 0;
            const hasLabel = labelRaw.length > 0;

            if (!hasScore && !hasLabel) {
                return;
            }

            if (!hasScore) {
                setInputValidation(scoreInput, "Enter the highest score for this grade.");
                isValid = false;
                return;
            }

            if (!hasLabel) {
                setInputValidation(labelInput, "Enter the grade name shown to students.");
                isValid = false;
            }

            const parsedScore = Number.parseFloat(scoreRaw);
            if (!Number.isFinite(parsedScore)) {
                setInputValidation(scoreInput, "Enter a valid score.");
                isValid = false;
                return;
            }

            if (parsedScore < 0) {
                setInputValidation(scoreInput, "Upper score must be 0 or higher.");
                isValid = false;
            }

            if (!hasAtMostOneDecimal(parsedScore)) {
                setInputValidation(scoreInput, "Use at most 1 decimal place, for example 0.5, 1, or 1.5.");
                isValid = false;
            }

            if (parsedScore > maxScore) {
                setInputValidation(scoreInput, `Upper score cannot be higher than the current maximum score of ${formatScore(maxScore)}.`);
                isValid = false;
            }

            if (labelRaw.length > 100) {
                setInputValidation(labelInput, "Grade name must be 100 characters or fewer.");
                isValid = false;
            }

            const normalizedGradeName = labelRaw.trim().toLowerCase();
            if (normalizedGradeName.length > 0) {
                if (usedGradeNames.has(normalizedGradeName)) {
                    setInputValidation(
                        labelInput,
                        `This grade name is already used in row ${usedGradeNames.get(normalizedGradeName) + 1}.`
                    );
                    isValid = false;
                } else {
                    usedGradeNames.set(normalizedGradeName, index);
                }
            }

            completedRows.push({
                index: index,
                scoreInput: scoreInput,
                labelInput: labelInput,
                maximumScore: Number.parseFloat(parsedScore.toFixed(1))
            });
        });

        if (completedRows.length === 0) {
            rows.slice(0, Math.min(2, rows.length)).forEach(function (row) {
                const scoreInput = row.querySelector("[data-grade-maximum]");
                const labelInput = row.querySelector('input[name$=".LabelPrimary"]');
                setInputValidation(scoreInput, "Type the highest score for this grade.");
                setInputValidation(labelInput, "Type the grade name students should see.");
            });

            setToggleValidation("Please fill in at least 2 grades. The light example text in the boxes is only a hint.");
            return false;
        }

        if (completedRows.length < 2) {
            setToggleValidation("Please fill in at least 2 complete grades. Each grade needs both a score and a grade name.");
            isValid = false;
        }

        for (let i = 1; i < completedRows.length; i += 1) {
            if (completedRows[i].maximumScore <= completedRows[i - 1].maximumScore) {
                setInputValidation(
                    completedRows[i].scoreInput,
                    "Each next 'Up to score' must be higher than the row above so grade ranges do not overlap."
                );
                isValid = false;
            }
        }

        const sortedRows = completedRows.slice().sort(function (a, b) {
            return a.maximumScore - b.maximumScore;
        });

        for (let i = 1; i < sortedRows.length; i += 1) {
            if (Math.abs(sortedRows[i].maximumScore - sortedRows[i - 1].maximumScore) < 0.0001) {
                setInputValidation(
                    sortedRows[i].scoreInput,
                    `This upper score is already used in row ${sortedRows[i - 1].index + 1}.`
                );
                isValid = false;
            }
        }

        const highestUpper = sortedRows[sortedRows.length - 1].maximumScore;
        if (Math.abs(highestUpper - maxScore) > 0.0001) {
            setInputValidation(
                sortedRows[sortedRows.length - 1].scoreInput,
                `The last grade must end at the current maximum score of ${formatScore(maxScore)} so every score gets a grade.`
            );
            isValid = false;
        }

        return isValid;
    }

    function validateWholeForm(root) {
        const form = document.getElementById("exam-settings-page-form");
        if (!form) {
            return true;
        }

        // Clear any previous custom-validation state before re-checking the form.
        clearGradeValidation(root);

        let isValid = true;

        if (window.jQuery && typeof window.jQuery === "function") {
            const validator = window.jQuery(form).data("validator");
            if (validator) {
                isValid = window.jQuery(form).valid() && isValid;
            }
        }

        if (!validateGradeSection(root)) {
            isValid = false;
        }

        const nativeValid = form.checkValidity();
        if (!nativeValid) {
            isValid = false;
        }

        return isValid;
    }

    function focusFirstInvalidField(root) {
        const form = document.getElementById("exam-settings-page-form");
        if (!form) {
            return;
        }

        const firstInvalid = form.querySelector(".input-validation-error, :invalid");
        if (!firstInvalid) {
            return;
        }

        if (typeof firstInvalid.focus === "function") {
            firstInvalid.focus({ preventScroll: true });
        }

        const target = firstInvalid.closest(".portal-field-group, .exam-rich-field, [data-grade-band-row], details") || firstInvalid;
        if (typeof target.scrollIntoView === "function") {
            target.scrollIntoView({ behavior: "smooth", block: "center" });
        }

        window.setTimeout(function () {
            if (typeof firstInvalid.reportValidity === "function") {
                firstInvalid.reportValidity();
            }
        }, 150);
    }

    function replaceIndex(value, nextIndex) {
        return value
            .replace(/ResultGradeBands_\d+__/g, `ResultGradeBands_${nextIndex}__`)
            .replace(/ResultGradeBands\[\d+\]/g, `ResultGradeBands[${nextIndex}]`);
    }

    function reindexGradeRows(tbody) {
        const rows = Array.from(tbody.querySelectorAll("[data-grade-band-row]"));

        rows.forEach(function (row, index) {
            row.querySelectorAll("input, label, span").forEach(function (element) {
                ["id", "name", "for", "data-valmsg-for"].forEach(function (attribute) {
                    const current = element.getAttribute(attribute);
                    if (current) {
                        element.setAttribute(attribute, replaceIndex(current, index));
                    }
                });
            });
        });
    }

    function showRowWarning(root, message) {
        const warning = root.querySelector("[data-grade-row-warning]");
        if (!warning) {
            return;
        }

        warning.textContent = message;
        warning.classList.remove("exam-hidden");
        window.clearTimeout(showRowWarning.timeoutId);
        showRowWarning.timeoutId = window.setTimeout(function () {
            warning.classList.add("exam-hidden");
        }, 3500);
    }

    function syncRemoveButtons(root) {
        const rows = root.querySelectorAll("[data-grade-band-row]");
        const minimumReached = rows.length <= 2;

        rows.forEach(function (row) {
            const button = row.querySelector("[data-remove-grade-row]");
            if (!button) {
                return;
            }

            button.setAttribute("aria-disabled", minimumReached ? "true" : "false");
            button.classList.toggle("grade-settings-remove-btn-disabled", minimumReached);
            button.title = minimumReached ? "At least 2 grades are required." : "Remove this grade";
        });
    }

    function addGradeRow(root) {
        const section = root.querySelector("[data-grade-settings-section]");
        const tbody = section ? section.querySelector("[data-grade-band-rows]") : null;
        const sourceRow = tbody ? tbody.querySelector("[data-grade-band-row]:last-child") : null;
        const maxScoreRaw = section ? Number.parseFloat(section.getAttribute("data-grade-max-score") || "") : Number.NaN;
        const maxScore = Number.isFinite(maxScoreRaw) ? maxScoreRaw : null;

        if (!tbody || !sourceRow) {
            return;
        }

        const nextIndex = tbody.querySelectorAll("[data-grade-band-row]").length;
        const clonedRow = sourceRow.cloneNode(true);

        clonedRow.querySelectorAll("input, label, span").forEach(function (element) {
            ["id", "name", "for", "data-valmsg-for"].forEach(function (attribute) {
                const current = element.getAttribute(attribute);
                if (current) {
                    element.setAttribute(attribute, replaceIndex(current, nextIndex));
                }
            });
        });

        clonedRow.querySelectorAll("input").forEach(function (input) {
            input.value = "";
            if (input.hasAttribute("value")) {
                input.setAttribute("value", "");
            }
        });

        clonedRow.querySelectorAll("[data-valmsg-for]").forEach(function (validationMessage) {
            validationMessage.textContent = "";
            validationMessage.classList.remove("field-validation-error");
            validationMessage.classList.add("field-validation-valid");
        });

        const preview = clonedRow.querySelector("[data-grade-range-preview]");
        if (preview) {
            preview.textContent = getEmptyRangePreview(maxScore);
        }

        tbody.appendChild(clonedRow);
        syncRemoveButtons(root);
    }

    function removeGradeRow(button, root) {
        const row = button.closest("[data-grade-band-row]");
        const section = root.querySelector("[data-grade-settings-section]");
        const tbody = section ? section.querySelector("[data-grade-band-rows]") : null;

        if (!row || !tbody) {
            return;
        }

        if (tbody.querySelectorAll("[data-grade-band-row]").length <= 2) {
            syncRemoveButtons(root);
            showRowWarning(root, "At least 2 grades are required.");
            return;
        }

        row.remove();
        reindexGradeRows(tbody);
        syncRemoveButtons(root);
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

    function updateGradeRanges(root) {
        const section = root.querySelector("[data-grade-settings-section]");
        if (!section) {
            return;
        }

        const rows = Array.from(section.querySelectorAll("[data-grade-band-row]"));
        const maxScoreRaw = Number.parseFloat(section.getAttribute("data-grade-max-score") || "");
        const maxScore = Number.isFinite(maxScoreRaw) ? maxScoreRaw : null;
        const entries = rows
            .map((row) => {
                const input = row.querySelector("[data-grade-maximum]");
                const raw = input ? Number.parseFloat(input.value) : Number.NaN;
                return {
                    row,
                    maximum: Number.isFinite(raw) ? raw : null
                };
            })
            .filter((entry) => entry.maximum !== null)
            .sort((a, b) => a.maximum - b.maximum);

        rows.forEach((row) => {
            const preview = row.querySelector("[data-grade-range-preview]");
            if (preview) {
                preview.textContent = getEmptyRangePreview(maxScore);
            }
        });

        entries.forEach((entry, index) => {
            const preview = entry.row.querySelector("[data-grade-range-preview]");
            if (!preview) {
                return;
            }

            const previousMaximum = index > 0 ? entries[index - 1].maximum : null;
            const lowerBound = previousMaximum === null
                ? 0
                : Number.parseFloat((previousMaximum + 0.1).toFixed(1));
            const upperBound = entry.maximum;

            if (maxScore !== null && lowerBound > maxScore) {
                preview.textContent = `${formatScore(lowerBound)}+`;
                return;
            }

            preview.textContent = upperBound === null || upperBound < lowerBound
                ? `${formatScore(lowerBound)}+`
                : `${formatScore(lowerBound)} - ${formatScore(upperBound)}`;
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        if (window.TinyMceHelpers) {
            TinyMceHelpers.configureJQueryValidationForTinyMce();
        }

        window.ExamRichEditFields?.init(document, {
            height: 260,
            min_height: 180,
            plugins: "lists link paste",
            toolbar: "undo redo | bold italic underline | bullist numlist | link | removeformat",
            paste_as_text: true
        });

        initializeSpecialNumberInputs(document);

        const gradeToggle = document.getElementById("settings-page-display-grade-on-results");
        const gradeSection = document.querySelector("[data-grade-settings-section]");
        if (gradeToggle && gradeSection) {
            const syncGradeSection = function () {
                gradeSection.classList.toggle("exam-hidden", !gradeToggle.checked);
            };

            gradeToggle.addEventListener("change", syncGradeSection);
            syncGradeSection();
        }

        document.addEventListener("input", function (event) {
            if (event.target && event.target.matches("[data-grade-maximum]")) {
                updateGradeRanges(document);
            }
        });

        const addGradeRowButton = document.querySelector("[data-add-grade-row]");
        if (addGradeRowButton) {
            addGradeRowButton.addEventListener("click", function () {
                addGradeRow(document);
                updateGradeRanges(document);
            });
        }

        document.addEventListener("click", function (event) {
            if (event.target && event.target.matches("[data-remove-grade-row]")) {
                event.preventDefault();
                removeGradeRow(event.target, document);
                updateGradeRanges(document);
            }
        });

        const form = document.getElementById("exam-settings-page-form");
        if (form) {
            form.addEventListener("submit", function (event) {
                window.ExamRichEditFields?.flush(form);

                if (!validateWholeForm(document)) {
                    event.preventDefault();
                    const summaryMessage = "Some settings still need attention. Please check the red fields and messages on this page.";
                    setFormSummary(document, summaryMessage);

                    const gradeSummary = document.querySelector("[data-grade-client-summary]");
                    if (gradeSummary && !gradeSummary.classList.contains("exam-hidden")) {
                        setClientSummary(document, "Please fix the custom grading section before saving.");
                    }

                    if (typeof window.notify === "function") {
                        window.notify(summaryMessage, { type: "warn", title: "Please check your settings" });
                    }

                    focusFirstInvalidField(document);
                    return;
                }

                clearGradeValidation(document);
            });
        }

        syncRemoveButtons(document);
        updateGradeRanges(document);
    });
})();
