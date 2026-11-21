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

    function showExamModal(missing) {
        const dialog = document.querySelector("[data-exam-submit-dialog]");
        if (!dialog || dialog.open) return Promise.resolve(false);
        dialog.querySelector("#examSubmitMessage").textContent = missing.length === 1
            ? dialog.dataset.singularMessage
            : dialog.dataset.pluralMessage.replace("{count}", String(missing.length));
        const list = dialog.querySelector("[data-exam-missing-list]");
        list.replaceChildren(...missing.map(number => {
            const item = document.createElement("li");
            item.textContent = dialog.dataset.questionMessage.replace("{number}", String(number));
            return item;
        }));
        dialog.returnValue = "";
        const buttons = Array.from(dialog.querySelectorAll("button"));
        const trapFocus = event => {
            if (event.key !== "Tab") return;
            if (event.shiftKey && document.activeElement === buttons[0]) {
                event.preventDefault();
                buttons.at(-1).focus();
            } else if (!event.shiftKey && document.activeElement === buttons.at(-1)) {
                event.preventDefault();
                buttons[0].focus();
            }
        };
        return new Promise(resolve => {
            dialog.addEventListener("keydown", trapFocus);
            dialog.addEventListener("close", () => {
                dialog.removeEventListener("keydown", trapFocus);
                resolve(dialog.returnValue === "confirm");
            }, { once: true });
            dialog.showModal();
            buttons[0].focus();
        });
    }
    function setActiveQuestionButton(questionId) {
        document.querySelectorAll(".question-nav-btn[data-question-id]").forEach(btn => {
            const btnId = Number(btn.dataset.questionId);
            const active = btnId === Number(questionId);
            btn.classList.toggle("is-active", active);
            btn.setAttribute("aria-current", active ? "page" : "false");
            if (active) {
                const progress = document.querySelector("[data-exam-progress]");
                if (progress) progress.textContent = progress.dataset.format.replace("{current}", btn.textContent.trim());
                const scroller = btn.closest("[data-exam-question-scroll]");
                if (scroller) {
                    const buttonRect = btn.getBoundingClientRect();
                    const scrollRect = scroller.getBoundingClientRect();
                    scroller.scrollLeft += buttonRect.left - scrollRect.left - (scrollRect.width - buttonRect.width) / 2;
                }
            }
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
        if (document.querySelector('[data-exam-progress]')) window.scrollTo({ top: 0, behavior: 'instant' });

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
            await saveInFlightPromise;
            return window.submitCurrentAnswer(options);
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

        let restoreSubmitFocus = false;
        submitInProgress = true;
        const submitBtn = document.querySelector("#submitExam");
        if (submitBtn) submitBtn.disabled = true;

        try {
            let answerSaveResult;
            try {
                answerSaveResult = await window.submitCurrentAnswer({ source: timeoutTriggered ? "timeout-submit" : "submit" });
            } catch {
                answerSaveResult = { success: false };
            }

            if (!timeoutTriggered && !answerSaveResult?.success) {
                const message = "Your latest answer could not be saved. Please try submitting again.";
                notify(message, { type: "error", title: "Submit failed" });
                return { success: false, errorMessage: message };
            }

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

                    const submitAnyway = await showExamModal(missing);

                    if (!submitAnyway) {
                        restoreSubmitFocus = true;
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
                if (restoreSubmitFocus) submitBtn?.focus({ preventScroll: true });
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


