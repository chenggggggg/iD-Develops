(function () {
    "use strict";

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value
            || document.getElementById("RequestVerificationToken")?.value
            || "";
    }

    function getTimePart(field, part) {
        return field.querySelector(`[data-exam-time-part="${part}"]`);
    }

    function setTimeEditorValue(field, canonical) {
        const match = /^(\d{1,5}):([0-5]\d):([0-5]\d)$/.exec(canonical || "");
        const hours = getTimePart(field, "hours");
        const minutes = getTimePart(field, "minutes");
        const seconds = getTimePart(field, "seconds");
        if (!hours || !minutes || !seconds) return;

        hours.value = match ? match[1] : "";
        minutes.value = match ? match[2] : "";
        seconds.value = match ? match[3] : "";
    }

    function parseTimeEditor(field) {
        const inputs = [
            getTimePart(field, "hours"),
            getTimePart(field, "minutes"),
            getTimePart(field, "seconds")
        ];

        if (inputs.some(input => !input)) {
            return { isValid: false, errorMessage: "The time editor is unavailable." };
        }

        inputs.forEach(input => input.removeAttribute("aria-invalid"));
        const rawValues = inputs.map(input => input.value.trim());
        if (rawValues.every(value => value === "")) {
            return { isValid: true, canonical: "", totalSeconds: null };
        }

        const values = rawValues.map(value => value === "" ? 0 : Number(value));
        const [hours, minutes, seconds] = values;
        const invalidInput = inputs.find((input, index) =>
            !Number.isInteger(values[index]) ||
            values[index] < Number(input.min) ||
            values[index] > Number(input.max));

        if (invalidInput) {
            invalidInput.setAttribute("aria-invalid", "true");
            return {
                isValid: false,
                invalidInput,
                errorMessage: "Hours must be positive, and minutes and seconds must be between 00 and 59."
            };
        }

        const totalSeconds = (hours * 3600) + (minutes * 60) + seconds;
        if (totalSeconds < 1 || totalSeconds > 2147483647) {
            inputs[0].setAttribute("aria-invalid", "true");
            return {
                isValid: false,
                invalidInput: inputs[0],
                errorMessage: "The time limit must be at least one second."
            };
        }

        return {
            isValid: true,
            canonical: `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`,
            totalSeconds
        };
    }

    function getSavedValue(field) {
        return field.getAttribute("data-exam-header-saved-value") || "";
    }

    function setSavedValue(field, value) {
        field.setAttribute("data-exam-header-saved-value", value || "");
    }

    function notify(message, options) {
        if (typeof window.notify === "function") {
            window.notify(message, options);
        }
    }

    function openEditor(field) {
        if (field.classList.contains("is-saving") || field.classList.contains("is-editing")) {
            return;
        }

        field.classList.add("is-editing");

        if (field.getAttribute("data-exam-header-edit") === "timeLimit") {
            field.style.width = `${Math.ceil(field.getBoundingClientRect().width)}px`;
            const hours = getTimePart(field, "hours");
            hours?.removeAttribute("aria-invalid");
            hours?.focus();
            hours?.select();
            return;
        }

        const input = field.querySelector("[data-exam-header-input]");
        if (!input) return;
        input.readOnly = false;
        input.removeAttribute("aria-invalid");
        input.focus();
        input.select();
    }

    function closeEditor(field) {
        const input = field.querySelector("[data-exam-header-input]");
        if (input) {
            input.readOnly = true;
        }

        field.classList.remove("is-editing");
        field.style.removeProperty("width");
    }

    function restoreSavedValue(field) {
        const savedValue = getSavedValue(field);

        if (field.getAttribute("data-exam-header-edit") === "timeLimit") {
            setTimeEditorValue(field, savedValue);
            field.querySelectorAll("[data-exam-time-part]")
                .forEach(input => input.removeAttribute("aria-invalid"));
            return;
        }

        const input = field.querySelector("[data-exam-header-input]");
        if (!input) {
            return;
        }

        input.value = savedValue;
        input.removeAttribute("aria-invalid");
    }

    async function saveField(field) {
        const display = field.querySelector("[data-exam-header-display]");
        const examId = field.getAttribute("data-exam-id");
        const editField = field.getAttribute("data-exam-header-edit");

        if (!examId || !editField || field.classList.contains("is-saving") ||
            (editField === "timeLimit" && !display)) {
            return;
        }

        const body = new URLSearchParams();
        body.set("__RequestVerificationToken", getToken());
        body.set("examId", examId);
        body.set("field", editField);

        let nextSavedValue;
        let focusTarget;
        if (editField === "name") {
            const input = field.querySelector("[data-exam-header-input]");
            if (!input) return;

            focusTarget = input;
            nextSavedValue = input.value.trim();
            if (!nextSavedValue) {
                input.setAttribute("aria-invalid", "true");
                notify("Give this exam a name before saving.", { type: "warn", title: "Name required" });
                input.focus();
                return;
            }

            body.set("name", nextSavedValue);
        } else if (editField === "timeLimit") {
            const parsedTime = parseTimeEditor(field);
            if (!parsedTime.isValid) {
                notify(parsedTime.errorMessage, { type: "warn", title: "Check time limit" });
                parsedTime.invalidInput?.focus();
                return;
            }

            focusTarget = getTimePart(field, "hours");
            nextSavedValue = parsedTime.canonical;
            body.set("timeLimitSeconds", parsedTime.totalSeconds === null ? "" : String(parsedTime.totalSeconds));
        } else {
            return;
        }

        if (nextSavedValue === getSavedValue(field)) {
            restoreSavedValue(field);
            closeEditor(field);
            return;
        }

        field.classList.add("is-saving");

        try {
            const response = await fetch(`${window.location.pathname}?handler=SaveHeaderSettings`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                    "RequestVerificationToken": getToken()
                },
                body
            });

            if (!response.ok) {
                throw new Error("Save failed.");
            }

            const result = await response.json();
            if (!result.success) {
                throw new Error(result.errorMessage || "Save failed.");
            }

            if (editField === "name") {
                const input = field.querySelector("[data-exam-header-input]");
                const savedName = result.name || nextSavedValue;
                input.value = savedName;
                input.size = Math.min(Math.max(savedName.length, 1), 60);
                setSavedValue(field, savedName);
                input.removeAttribute("aria-invalid");
            } else {
                const savedTime = result.timeLimitDisplay || "";
                const hasTimeLimit = Number(result.timeLimitSeconds) > 0;
                setTimeEditorValue(field, hasTimeLimit ? savedTime : "");
                display.textContent = hasTimeLimit ? savedTime : "No time limit";
                setSavedValue(field, hasTimeLimit ? savedTime : "");
                field.querySelectorAll("[data-exam-time-part]")
                    .forEach(input => input.removeAttribute("aria-invalid"));
            }

            closeEditor(field);
            notify("Exam settings saved.", { type: "success", title: "Saved" });
        } catch (error) {
            notify(error.message || "Could not save exam settings.", { type: "error", title: "Save failed" });
            focusTarget?.focus();
        } finally {
            field.classList.remove("is-saving");
        }
    }

    document.addEventListener("click", function (event) {
        const field = event.target.closest("[data-exam-header-edit]");
        if (!field || field.classList.contains("is-editing")) {
            return;
        }

        openEditor(field);
    });

    document.addEventListener("focusin", function (event) {
        const timePart = event.target.closest("[data-exam-time-part]");
        timePart?.select();
    });

    document.addEventListener("keydown", function (event) {
        const control = event.target.closest("[data-exam-header-input], [data-exam-time-part]");
        if (!control) {
            return;
        }

        const field = control.closest("[data-exam-header-edit]");
        if (!field) {
            return;
        }

        if (event.key === "Enter") {
            event.preventDefault();
            saveField(field);
        } else if (event.key === "Escape") {
            event.preventDefault();
            field.dataset.skipNextHeaderSave = "true";
            restoreSavedValue(field);
            closeEditor(field);
            control.blur();
        }
    });

    document.addEventListener("focusout", function (event) {
        const control = event.target.closest("[data-exam-header-input], [data-exam-time-part]");
        if (!control) {
            return;
        }

        const field = control.closest("[data-exam-header-edit]");
        if (!field) {
            return;
        }

        window.setTimeout(function () {
            if (field.dataset.skipNextHeaderSave === "true") {
                delete field.dataset.skipNextHeaderSave;
                return;
            }

            if (!field.contains(document.activeElement) && field.classList.contains("is-editing")) {
                saveField(field);
            }
        }, 100);
    });
})();
