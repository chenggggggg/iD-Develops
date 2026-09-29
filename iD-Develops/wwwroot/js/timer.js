let timerEnded = false;

function showTimerEndMessage() {
    const message = "Exam time is over. Submitting your latest saved answers now...";

    if (typeof notify === "function") {
        notify(message, { type: "warn", title: "Time limit reached" });
    }

    let banner = document.getElementById("exam-timeout-banner");
    if (!banner) {
        banner = document.createElement("div");
        banner.id = "exam-timeout-banner";
        banner.className = "exam-timeout-banner";
        banner.style.position = "fixed";
        banner.style.top = "56px";
        banner.style.left = "0";
        banner.style.right = "0";
        banner.style.zIndex = "1055";
        document.body.appendChild(banner);
    }

    banner.textContent = message;
}

async function handleTimerEnd() {
    if (timerEnded) return;
    timerEnded = true;

    showTimerEndMessage();

    const submitBtn = document.getElementById("submitExam");
    if (submitBtn) submitBtn.disabled = true;

    try {
        // Best-effort last save. If it fails, server still keeps latest valid saved state.
        try { await window.submitCurrentAnswer?.(); } catch { /* ignore */ }

        const response = await window.submitExam?.({ force: true, timeoutTriggered: true });

        if (response?.success && response.redirectUrl) {
            window.location.href = response.redirectUrl;
            return;
        }

        // Fallback: reload to let server-side status check redirect to completion.
        setTimeout(() => window.location.reload(), 1200);
    } catch {
        setTimeout(() => window.location.reload(), 1200);
    }
}

function initializeTimer(elementId, endTime) {
    const timerElement = document.getElementById(elementId);

    if (!timerElement) {
        return;
    }

    async function updateTimer() {
        const now = new Date();
        const utcNow = new Date(now.getTime() + now.getTimezoneOffset() * 60000);

        const remainingTime = endTime - utcNow;

        if (remainingTime <= 0) {
            timerElement.textContent = "00:00:00";
            clearInterval(timerInterval);
            await handleTimerEnd();
            return;
        }

        const hours = Math.floor((remainingTime % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
        const minutes = Math.floor((remainingTime % (1000 * 60 * 60)) / (1000 * 60));
        const seconds = Math.floor((remainingTime % (1000 * 60)) / 1000);

        timerElement.textContent =
            `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
    }

    const timerInterval = setInterval(updateTimer, 1000);
    updateTimer();
}

document.addEventListener("DOMContentLoaded", () => {
    const timerElement = document.getElementById("countdown");
    if (!timerElement) return;

    const rawEnd = timerElement.getAttribute("data-duration");
    if (!rawEnd) return;

    const parsedEnd = new Date(rawEnd);
    if (Number.isNaN(parsedEnd.getTime())) {
        return;
    }

    initializeTimer("countdown", parsedEnd);
});
