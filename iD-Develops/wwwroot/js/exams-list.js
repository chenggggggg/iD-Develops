function initializeExamsList() {
    initializeExamActionPopovers();

    initializeExamActionConfirmation();
    initializeExamLaunchDialog();

    const successMessage = sessionStorage.getItem("successMessage");
    if (!successMessage) {
        return;
    }

    displayDivMessage(successMessage, true);
    sessionStorage.removeItem("successMessage");
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeExamsList, { once: true });
} else {
    initializeExamsList();
}

function initializeExamActionConfirmation() {
    const dialog = document.getElementById("examActionConfirmDialog");
    if (!dialog) return;

    const title = dialog.querySelector("[data-exam-confirm-title]");
    const message = dialog.querySelector("[data-exam-confirm-message]");
    const confirmButton = dialog.querySelector("[data-exam-confirm-submit]");
    const confirmLabel = dialog.querySelector("[data-exam-confirm-submit-label]");
    const confirmIcon = confirmButton?.querySelector("i");
    const closeButton = dialog.querySelector('[data-portal-dialog-close="examActionConfirmDialog"]');
    let pendingButton = null;

    document.querySelectorAll("[data-exam-confirm-action]").forEach(function (button) {
        button.addEventListener("click", function () {
            pendingButton = button;
            const action = button.dataset.examConfirmAction;
            const examName = button.dataset.examName || "this exam";

            button.closest("[data-exam-actions-popover]")?.hidePopover?.();

            if (action === "archive") {
                title.textContent = "Archive exam";
                message.textContent = `Archive “${examName}”? Students will no longer be able to start it, while existing attempts and results remain available.`;
                confirmLabel.textContent = "Archive exam";
                confirmButton.classList.remove("portal-btn-danger");
                confirmButton.classList.add("portal-btn-primary");
                confirmIcon?.classList.remove("fa-trash", "fa-triangle-exclamation");
                confirmIcon?.classList.add("fa-box-archive");
            } else {
                title.textContent = "Delete exam";
                message.textContent = `Permanently delete “${examName}”? This action cannot be undone.`;
                confirmLabel.textContent = "Delete exam";
                confirmButton.classList.remove("portal-btn-primary");
                confirmButton.classList.add("portal-btn-danger");
                confirmIcon?.classList.remove("fa-box-archive", "fa-triangle-exclamation");
                confirmIcon?.classList.add("fa-trash");
            }
        });
    });

    confirmButton?.addEventListener("click", async function () {
        if (!pendingButton || confirmButton.disabled) return;

        const action = pendingButton.dataset.examConfirmAction;
        confirmButton.disabled = true;

        if (action === "archive") {
            const form = pendingButton.closest("form[data-exam-archive-form]");
            if (form) {
                form.requestSubmit();
                return;
            }
        } else if (action === "delete") {
            const wasDeleted = await deleteExam(pendingButton);
            if (wasDeleted) {
                closeButton?.click();
                pendingButton = null;
            }
        }

        confirmButton.disabled = false;
    });

    dialog.querySelectorAll('[data-portal-dialog-close="examActionConfirmDialog"]').forEach(function (button) {
        button.addEventListener("click", function () {
            pendingButton = null;
            if (confirmButton) confirmButton.disabled = false;
        });
    });
}

function initializeExamActionPopovers() {
    const popovers = Array.from(document.querySelectorAll("[data-exam-actions-popover]"));
    if (!popovers.length) return;

    function positionPopover(popover) {
        const trigger = document.querySelector(`[data-exam-actions-trigger="${CSS.escape(popover.id)}"]`);
        if (!trigger || !popover.matches(":popover-open")) return;

        const gutter = 8;
        const gap = 6;
        const triggerBounds = trigger.getBoundingClientRect();
        const popoverBounds = popover.getBoundingClientRect();
        const left = Math.min(
            window.innerWidth - popoverBounds.width - gutter,
            Math.max(gutter, triggerBounds.right - popoverBounds.width));
        const spaceBelow = window.innerHeight - triggerBounds.bottom - gutter;
        const top = spaceBelow >= popoverBounds.height + gap
            ? triggerBounds.bottom + gap
            : Math.max(gutter, triggerBounds.top - popoverBounds.height - gap);

        popover.style.left = `${left}px`;
        popover.style.top = `${top}px`;
    }

    popovers.forEach(function (popover) {
        popover.addEventListener("toggle", function (event) {
            if (event.newState === "open") {
                window.requestAnimationFrame(function () { positionPopover(popover); });
            }
        });
    });

    function repositionOpenPopovers() {
        popovers.forEach(positionPopover);
    }

    window.addEventListener("resize", repositionOpenPopovers);
    document.addEventListener("scroll", repositionOpenPopovers, true);
}

function initializeExamLaunchDialog() {
    const dialog = document.getElementById("examLaunchDialog");
    if (!dialog) return;

    const loading = dialog.querySelector("[data-exam-launch-loading]");
    const error = dialog.querySelector("[data-exam-launch-error]");
    const errorText = dialog.querySelector("[data-exam-launch-error-text]");
    const content = dialog.querySelector("[data-exam-launch-content]");
    const title = dialog.querySelector("[data-exam-launch-title]");
    const course = dialog.querySelector("[data-exam-launch-course]");
    const time = dialog.querySelector("[data-exam-launch-time]");
    const attempts = dialog.querySelector("[data-exam-launch-attempts]");
    const introduction = dialog.querySelector("[data-exam-launch-introduction]");
    const state = dialog.querySelector("[data-exam-launch-state]");
    const stateText = dialog.querySelector("[data-exam-launch-state-text]");
    const primaryForm = dialog.querySelector("[data-exam-launch-primary-form]");
    const primaryId = dialog.querySelector("[data-exam-launch-primary-id]");
    const launchAction = dialog.querySelector("[data-exam-launch-action]");
    const submitLabel = dialog.querySelector("[data-exam-launch-submit-label]");
    const restartForm = dialog.querySelector("[data-exam-launch-restart-form]");
    const restartId = dialog.querySelector("[data-exam-launch-restart-id]");
    let activeRequest = null;

    function resetDialog() {
        if (activeRequest) activeRequest.abort();
        activeRequest = new AbortController();

        loading.hidden = false;
        error.hidden = true;
        content.hidden = true;
        primaryForm.hidden = true;
        restartForm.hidden = true;
        title.textContent = "Exam details";
    }

    function showState(message) {
        state.hidden = !message;
        stateText.textContent = message || "";
    }

    function renderLaunchInfo(info) {
        title.textContent = info.title || "Exam details";
        course.textContent = info.courseName || "Not specified";
        time.textContent = Number(info.timeLimitSeconds) > 0
            ? formatDuration(info.timeLimitSeconds)
            : "No time limit";

        if (!info.isPublished) {
            attempts.textContent = "Unavailable";
        } else if (info.hasUnlimitedAttempts) {
            attempts.textContent = "Unlimited";
        } else {
            attempts.textContent = `${info.attemptsLeft} of ${info.maxAttempts} remaining`;
        }

        introduction.innerHTML = info.introductionHtml
            || "<p>No introduction has been provided for this exam.</p>";

        primaryForm.hidden = true;
        restartForm.hidden = true;

        if (info.canContinue) {
            primaryId.value = String(info.examId);
            launchAction.value = "continue";
            submitLabel.textContent = "Continue exam";
            primaryForm.hidden = false;

            showState(info.canRestart
                ? "You have an attempt in progress. Continue it, or restart using another attempt."
                : "You have an attempt in progress."
            );
        } else if (info.canStart) {
            primaryId.value = String(info.examId);
            launchAction.value = "start";
            submitLabel.textContent = "Start exam";
            primaryForm.hidden = false;
            showState("");
        } else {
            showState(info.blockReason || "This exam cannot be started right now.");
        }

        if (info.canRestart) {
            restartId.value = String(info.examId);
            restartForm.hidden = false;
        }

        loading.hidden = true;
        error.hidden = true;
        content.hidden = false;
    }

    async function loadLaunchInfo(examId) {
        resetDialog();

        try {
            const response = await fetch(
                `${window.location.pathname}?handler=LaunchInfo&examId=${encodeURIComponent(examId)}`,
                {
                    headers: { "X-Requested-With": "XMLHttpRequest" },
                    credentials: "same-origin",
                    cache: "no-store",
                    signal: activeRequest.signal
                }
            );

            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) {
                throw new Error(result?.errorMessage || "Exam details could not be loaded.");
            }

            renderLaunchInfo(result);
        } catch (requestError) {
            if (requestError?.name === "AbortError") return;

            loading.hidden = true;
            content.hidden = true;
            errorText.textContent = requestError?.message || "Exam details could not be loaded.";
            error.hidden = false;
        }
    }

    document.querySelectorAll("[data-exam-launch]").forEach(function (button) {
        button.addEventListener("click", function () {
            loadLaunchInfo(button.dataset.examId);
        });
    });

    dialog.querySelectorAll("[data-exam-launch-primary-form], [data-exam-launch-restart-form]")
        .forEach(function (form) {
            form.addEventListener("submit", function (event) {
                if (event.defaultPrevented) return;

                form.querySelectorAll("button[type='submit']").forEach(function (button) {
                    button.disabled = true;
                });
            });
        });
}

function formatDuration(rawSeconds) {
    const totalSeconds = Math.max(0, Number.parseInt(rawSeconds, 10) || 0);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}
