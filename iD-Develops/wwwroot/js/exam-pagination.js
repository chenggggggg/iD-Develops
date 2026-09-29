// wwwroot/js/exam-pagination.js
(function () {
    "use strict";

    let submitInProgress = false;
    let autosaveTimer = null;
    let saveInFlightPromise = null;
    let lastSavedFingerprint = null;

    function getAntiForgeryToken() {
        const tokenInput =
            document.querySelector('input[name="__RequestVerificationToken"]')
            || document.querySelector("#RequestVerificationToken");

        return tokenInput ? tokenInput.value : null;
    }

    function getPagePath() {
        return window.location.pathname;
    }

    function getQuestionHost() {
        return document.querySelector("#questionHost")
            || document.querySelector("#questionContainer")
            || document.querySelector("[data-question-host]");
    }

    function ensureExamModalStyles() {
        if (document.getElementById("exam-modal-styles")) return;

        const style = document.createElement("style");
        style.id = "exam-modal-styles";
        style.textContent = `
.exam-modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.62);
  z-index: 40000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.exam-modal {
  width: min(560px, 100%);
  background: #ffffff;
  border-radius: 14px;
  box-shadow: 0 24px 60px rgba(0, 0, 0, 0.35);
  overflow: hidden;
}

.exam-modal-header {
  padding: 16px 18px 10px 18px;
  border-bottom: 1px solid #e9ecef;
}

.exam-modal-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: #111827;
}

.exam-modal-body {
  padding: 14px 18px;
  color: #1f2937;
  line-height: 1.45;
}

.exam-modal-body ul {
  margin: 10px 0 0 18px;
  padding: 0;
}

.exam-modal-body li + li {
  margin-top: 6px;
}

.exam-modal-footer {
  padding: 12px 18px 16px 18px;
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  border-top: 1px solid #e9ecef;
}

.exam-modal-btn {
  border: 1px solid transparent;
  border-radius: 10px;
  padding: 8px 14px;
  font-weight: 600;
  font-size: 0.92rem;
  cursor: pointer;
}

.exam-modal-btn-secondary {
  background: #ffffff;
  color: #1f2937;
  border-color: #d1d5db;
}

.exam-modal-btn-primary {
  background: #1d4ed8;
  color: #ffffff;
}

.exam-modal-btn-danger {
  background: #b91c1c;
  color: #ffffff;
}
`;

        document.head.appendChild(style);
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function showExamModal(options) {
        ensureExamModalStyles();

        const opts = options || {};
        const title = opts.title || "Confirmation";
        const message = opts.message || "";
        const details = Array.isArray(opts.details) ? opts.details : [];
        const confirmText = opts.confirmText || "Continue";
        const cancelText = opts.cancelText || "Cancel";
        const hideCancel = !!opts.hideCancel;
        const confirmVariant = (opts.confirmVariant || "primary").toLowerCase();

        return new Promise((resolve) => {
            const overlay = document.createElement("div");
            overlay.className = "exam-modal-overlay";
            overlay.setAttribute("role", "presentation");

            const confirmClass = confirmVariant === "danger"
                ? "exam-modal-btn-danger"
                : "exam-modal-btn-primary";

            const detailList = details.length > 0
                ? `<ul>${details.map((x) => `<li>${escapeHtml(x)}</li>`).join("")}</ul>`
                : "";

            overlay.innerHTML = `
<div class="exam-modal" role="dialog" aria-modal="true" aria-labelledby="examModalTitle">
  <div class="exam-modal-header">
    <h3 class="exam-modal-title" id="examModalTitle">${escapeHtml(title)}</h3>
  </div>
  <div class="exam-modal-body">
    <div>${escapeHtml(message)}</div>
    ${detailList}
  </div>
  <div class="exam-modal-footer">
    ${hideCancel ? "" : `<button type="button" class="exam-modal-btn exam-modal-btn-secondary" data-action="cancel">${escapeHtml(cancelText)}</button>`}
    <button type="button" class="exam-modal-btn ${confirmClass}" data-action="confirm">${escapeHtml(confirmText)}</button>
  </div>
</div>`;

            const close = (value) => {
                overlay.remove();
                resolve(!!value);
            };

            overlay.addEventListener("click", (event) => {
                const actionEl = event.target.closest("[data-action]");
                if (!actionEl) return;
                if (actionEl.dataset.action === "confirm") close(true);
                if (actionEl.dataset.action === "cancel") close(false);
            });

            document.addEventListener("keydown", function onKeyDown(event) {
                if (!document.body.contains(overlay)) {
                    document.removeEventListener("keydown", onKeyDown);
                    return;
                }

                if (event.key === "Escape" && !hideCancel) {
                    event.preventDefault();
                    document.removeEventListener("keydown", onKeyDown);
                    close(false);
                }
            });

            document.body.appendChild(overlay);
        });
    }

    function setActiveQuestionButton(questionId) {
        document.querySelectorAll(".question-nav-btn[data-question-id]").forEach(btn => {
            const btnId = Number(btn.dataset.questionId);
            const active = btnId === Number(questionId);
            btn.classList.toggle("is-active", active);
            btn.setAttribute("aria-current", active ? "page" : "false");
        });
    }

    function getCurrentQuestionIdFromDom() {
        const qid =
            document.querySelector("#questionId")
            || document.querySelector('input[name="questionId"]')
            || document.querySelector("#currentQuestionId")
            || document.querySelector('[data-current-question-id]');

        if (!qid) return null;
        if (qid.matches("input")) return Number(qid.value);
        return Number(qid.getAttribute("data-current-question-id"));
    }

    function getExamIdFromDom() {
        const el = document.querySelector('input[name="examId"]') || document.querySelector("#ExamId") || document.querySelector("#examId");
        return el ? Number(el.value) : null;
    }

    function getRecordIdFromDom() {
        const el = document.querySelector('input[name="recordId"]') || document.querySelector("#RecordId") || document.querySelector("#recordId");
        return el ? el.value : null;
    }

    function getRawCurrentAnswer() {
        const checkedRadio = document.querySelector('input[type="radio"][name="answer"]:checked');
        if (checkedRadio) return checkedRadio.value ?? "";

        const checkedCheckboxes = Array.from(
            document.querySelectorAll('input[type="checkbox"][name="answer"]:checked')
        );
        if (checkedCheckboxes.length > 0) {
            return checkedCheckboxes.map(x => x.value).join(",");
        }

        const answerField = document.querySelector('[name="answer"]');
        if (answerField && !answerField.matches('input[type="radio"], input[type="checkbox"]')) {
            return answerField.value ?? "";
        }

        return "";
    }

    function extractAnswerPayload() {
        const questionId = getCurrentQuestionIdFromDom();
        const examId = getExamIdFromDom();
        const recordId = getRecordIdFromDom();

        if (!questionId || !examId || !recordId) {
            return { ok: false, message: "Missing questionId/examId/recordId in DOM." };
        }

        return {
            ok: true,
            questionId,
            examId,
            recordId,
            answer: getRawCurrentAnswer()
        };
    }

    function fingerprintPayload(payload) {
        return `${payload.questionId}|${payload.examId}|${payload.recordId}|${payload.answer ?? ""}`;
    }

    async function postForm(url, formData) {
        const token = getAntiForgeryToken();

        const headers = {
            "X-Requested-With": "XMLHttpRequest",
            "Accept": "application/json"
        };

        if (token) headers["RequestVerificationToken"] = token;

        try {
            const res = await fetch(url, {
                method: "POST",
                headers,
                body: formData,
                credentials: "same-origin"
            });

            if (!res.ok) {
                window.ExamConnectivity?.handleFailure(
                    res,
                    null,
                    "Saving answers is temporarily unavailable."
                );
                return res;
            }

            window.ExamConnectivity?.noteSuccess?.();
            return res;
        } catch (err) {
            window.ExamConnectivity?.handleFailure(
                null,
                err,
                "Connection lost. Trying to reconnect..."
            );
            throw err;
        }
    }

    async function fetchPartial(url) {
        try {
            const res = await fetch(url, {
                method: "GET",
                headers: {
                    "X-Requested-With": "XMLHttpRequest",
                    "Accept": "text/html"
                },
                credentials: "same-origin"
            });

            if (!res.ok) {
                window.ExamConnectivity?.handleFailure(
                    res,
                    null,
                    "Unable to load question right now."
                );
                return null;
            }

            window.ExamConnectivity?.noteSuccess?.();
            return await res.text();
        } catch (err) {
            window.ExamConnectivity?.handleFailure(
                null,
                err,
                "Connection lost while loading question."
            );
            throw err;
        }
    }

    async function submitAnswerCore(source) {
        const payload = extractAnswerPayload();
        if (!payload.ok) return { success: false, message: payload.message };

        const fingerprint = fingerprintPayload(payload);
        if (fingerprint === lastSavedFingerprint) {
            return { success: true, skipped: true };
        }

        const url = `${getPagePath()}?handler=SubmitAnswer`;

        const formData = new FormData();
        formData.append("questionId", String(payload.questionId));
        formData.append("examId", String(payload.examId));
        formData.append("recordId", String(payload.recordId));
        formData.append("answer", payload.answer ?? "");
        formData.append("source", source || "manual");

        const res = await postForm(url, formData);
        if (!res || !res.ok) return { success: false, status: res?.status };

        const json = await res.json();
        if (json?.success) {
            lastSavedFingerprint = fingerprint;
        }

        return json;
    }

    function queueAutosave(delayMs) {
        if (autosaveTimer) {
            clearTimeout(autosaveTimer);
            autosaveTimer = null;
        }

        autosaveTimer = setTimeout(async () => {
            autosaveTimer = null;
            try {
                await window.submitCurrentAnswer({ source: "autosave" });
            } catch {
                // best-effort autosave
            }
        }, delayMs);
    }

    function wireTakeAutosave() {
        document.addEventListener("input", (e) => {
            const el = e.target;
            if (!el) return;
            if (el.matches('textarea[name="answer"], input[type="text"][name="answer"]')) {
                queueAutosave(1200);
            }
        });

        document.addEventListener("change", (e) => {
            const el = e.target;
            if (!el) return;
            if (el.matches('input[type="radio"][name="answer"], input[type="checkbox"][name="answer"]')) {
                queueAutosave(0);
            }
        });

        document.addEventListener("blur", (e) => {
            const el = e.target;
            if (!el) return;
            if (el.matches('textarea[name="answer"], input[type="text"][name="answer"]')) {
                queueAutosave(0);
            }
        }, true);
    }

    window.loadQuestion = async function (questionId) {
        try { await window.submitCurrentAnswer({ source: "navigation" }); } catch { }

        const host = getQuestionHost();
        if (!host) return;

        const examId = getExamIdFromDom();
        const recordId = getRecordIdFromDom();

        const url =
            `${getPagePath()}?handler=QuestionShell` +
            `&questionId=${encodeURIComponent(questionId)}` +
            (examId ? `&examId=${encodeURIComponent(examId)}` : "") +
            (recordId ? `&recordId=${encodeURIComponent(recordId)}` : "");

        const html = await fetchPartial(url);
        if (!html) return;

        host.innerHTML = html;
        setActiveQuestionButton(questionId);

        try {
            const u = new URL(window.location.href);
            u.searchParams.set("questionId", String(questionId));
            history.replaceState({}, "", u.toString());
        } catch { }

        const payload = extractAnswerPayload();
        if (payload.ok) {
            lastSavedFingerprint = fingerprintPayload(payload);
        }
    };

    window.submitCurrentAnswer = async function (options = {}) {
        const source = options.source || "manual";

        if (saveInFlightPromise) {
            return saveInFlightPromise;
        }

        saveInFlightPromise = submitAnswerCore(source)
            .finally(() => {
                saveInFlightPromise = null;
            });

        return saveInFlightPromise;
    };

    window.submitExam = async function (options = {}) {
        const force = !!options.force;
        const timeoutTriggered = !!options.timeoutTriggered;

        if (submitInProgress) {
            return { success: false, errorMessage: "Submission already in progress." };
        }

        const examId = getExamIdFromDom();
        const recordId = getRecordIdFromDom();

        if (!examId || !recordId) {
            notify("Cannot submit: examId or recordId missing.", { type: "error", title: "Error" });
            return { success: false, errorMessage: "Missing examId or recordId." };
        }

        submitInProgress = true;
        const submitBtn = document.querySelector("#submitExam");
        if (submitBtn) submitBtn.disabled = true;

        try {
            try { await window.submitCurrentAnswer({ source: timeoutTriggered ? "timeout-submit" : "submit" }); } catch { }

            if (!force) {
                const validateUrl = `${getPagePath()}?handler=ValidateMissingQuestions`;
                const validateForm = new FormData();
                validateForm.append("examId", String(examId));
                validateForm.append("recordId", String(recordId));

                const validateRes = await postForm(validateUrl, validateForm);
                if (!validateRes || !validateRes.ok) {
                    return { success: false, errorMessage: "Could not validate unanswered questions." };
                }

                const validateJson = await validateRes.json();
                if (!validateJson.success) {
                    const missing = Array.isArray(validateJson.missingQuestionNumbers)
                        ? validateJson.missingQuestionNumbers
                            .map(Number)
                            .filter((x) => Number.isFinite(x) && x > 0)
                        : [];

                    const details = missing.map((questionNumber) => `Question ${questionNumber} is not answered.`);
                    const message = missing.length === 1
                        ? "One question is still unanswered. Do you want to submit anyway?"
                        : `${missing.length} questions are still unanswered. Do you want to submit anyway?`;

                    const submitAnyway = await showExamModal({
                        title: "Incomplete examination",
                        message,
                        details,
                        confirmText: "Submit anyway",
                        cancelText: "Cancel",
                        confirmVariant: "danger"
                    });

                    if (!submitAnyway) {
                        return { success: false, errorMessage: "Unanswered questions remain." };
                    }
                }
            }

            const submitUrl = `${getPagePath()}?handler=SubmitExam`;
            const submitForm = new FormData();
            submitForm.append("examId", String(examId));
            submitForm.append("recordId", String(recordId));

            const submitRes = await postForm(submitUrl, submitForm);
            if (!submitRes || !submitRes.ok) {
                return { success: false, errorMessage: "Submit request failed." };
            }

            const submitJson = await submitRes.json();
            if (submitJson.success && submitJson.redirectUrl) {
                window.location.href = submitJson.redirectUrl;
                return submitJson;
            }

            notify(submitJson.errorMessage ?? "Submit failed.", { type: "error", title: "Submit failed" });
            return { success: false, errorMessage: submitJson.errorMessage ?? "Submit failed." };
        } finally {
            if (!timeoutTriggered) {
                submitInProgress = false;
                if (submitBtn) submitBtn.disabled = false;
            }
        }
    };

    document.addEventListener("click", (e) => {
        const btn = e.target.closest(".question-nav-btn[data-action='load-question']");
        if (!btn) return;

        const qid = Number(btn.dataset.questionId);
        if (!Number.isFinite(qid) || qid <= 0) return;

        window.loadQuestion(qid);
    });

    document.addEventListener("DOMContentLoaded", async function () {
        const submitBtn = document.querySelector("#submitExam");
        if (submitBtn) {
            submitBtn.addEventListener("click", function () {
                window.submitExam();
            });
        }

        const currentQid = getCurrentQuestionIdFromDom();
        if (currentQid) {
            setActiveQuestionButton(currentQid);
            const payload = extractAnswerPayload();
            if (payload.ok) {
                lastSavedFingerprint = fingerprintPayload(payload);
            }
        }

        wireTakeAutosave();
    });
})();


