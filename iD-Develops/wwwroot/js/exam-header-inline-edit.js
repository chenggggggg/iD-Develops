(function () {
    "use strict";

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value
            || document.getElementById("RequestVerificationToken")?.value
            || "";
    }

    function normalizeTimeLimit(raw) {
        const value = Number.parseInt((raw || "").trim(), 10);
        return Number.isFinite(value) && value > 0 ? String(value) : "";
    }

    function openEditor(field) {
        const input = field.querySelector("[data-exam-header-input]");
        if (!input) {
            return;
        }

        field.classList.add("is-editing");
        input.focus();
        input.select();
    }

    function closeEditor(field) {
        field.classList.remove("is-editing");
    }

    async function saveField(field) {
        const input = field.querySelector("[data-exam-header-input]");
        const display = field.querySelector("[data-exam-header-display]");
        const examId = field.getAttribute("data-exam-id");
        const editField = field.getAttribute("data-exam-header-edit");

        if (!input || !display || !examId || !editField) {
            return;
        }

        const body = new URLSearchParams();
        body.set("__RequestVerificationToken", getToken());
        body.set("examId", examId);
        body.set("field", editField);

        if (editField === "name") {
            body.set("name", input.value.trim());
        } else if (editField === "timeLimit") {
            const normalizedTime = normalizeTimeLimit(input.value);
            if (normalizedTime) {
                body.set("timeLimit", normalizedTime);
            }
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
                input.value = result.name || "untitled-exam";
                display.textContent = input.value;
            } else {
                input.value = result.timeLimit || "";
                display.textContent = result.timeLimitDisplay || "No time limit";
            }

            if (typeof window.notify === "function") {
                window.notify("Exam settings saved.", { type: "success", title: "Saved" });
            }
        } catch (error) {
            if (typeof window.notify === "function") {
                window.notify(error.message || "Could not save exam settings.", { type: "error", title: "Save failed" });
            }
        } finally {
            field.classList.remove("is-saving");
            closeEditor(field);
        }
    }

    document.addEventListener("click", function (event) {
        const field = event.target.closest("[data-exam-header-edit]");
        if (!field) {
            return;
        }

        if (!field.classList.contains("is-editing")) {
            openEditor(field);
        }
    });

    document.addEventListener("keydown", function (event) {
        const input = event.target.closest("[data-exam-header-input]");
        if (!input) {
            return;
        }

        const field = input.closest("[data-exam-header-edit]");
        if (!field) {
            return;
        }

        if (event.key === "Enter") {
            event.preventDefault();
            saveField(field);
        } else if (event.key === "Escape") {
            closeEditor(field);
        }
    });

    document.addEventListener("focusout", function (event) {
        const input = event.target.closest("[data-exam-header-input]");
        if (!input) {
            return;
        }

        const field = input.closest("[data-exam-header-edit]");
        if (!field) {
            return;
        }

        window.setTimeout(function () {
            if (!field.contains(document.activeElement)) {
                saveField(field);
            }
        }, 100);
    });
})();
