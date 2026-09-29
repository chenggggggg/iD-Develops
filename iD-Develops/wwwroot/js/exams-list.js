document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("[data-delete-exam]").forEach(function (button) {
        button.addEventListener("click", function (event) {
            event.preventDefault();
            deleteExam(button);
        });
    });

    initializeExamLaunchDialog();

    const successMessage = sessionStorage.getItem("successMessage");
    if (!successMessage) {
        return;
    }

    displayDivMessage(successMessage, true);
    sessionStorage.removeItem("successMessage");
});

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
        time.textContent = Number(info.timeLimit) > 0
            ? `${info.timeLimit} minutes`
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

    restartForm.addEventListener("submit", function (event) {
        const confirmed = window.confirm(
            "Restarting will end your current attempt and use another attempt. Continue?"
        );

        if (!confirmed) event.preventDefault();
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
