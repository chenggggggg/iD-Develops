// wwwroot/js/exam-editor.js
(() => {
    "use strict";

    function pagePath() { return window.location.pathname; }

    function getExamId() {
        const el = document.querySelector("#ExamId") || document.querySelector('input[name="ExamId"]');
        const v = el ? Number(el.value) : NaN;
        return Number.isFinite(v) ? v : null;
    }

    function getQuestionHost() {
        return document.querySelector("#questionHost")
            || document.querySelector("#questionContainer")
            || document.querySelector("[data-question-host]");
    }

    function getAntiForgeryToken(form) {
        // Prefer the token from the form we are actually submitting (autosave)
        const scoped = form?.querySelector?.('input[name="__RequestVerificationToken"]')?.value;
        if (scoped) return scoped;

        // Fallback (e.g. other POST forms)
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? null;
    }

    function normalizeKind(kind) {
        const k = (kind || "").trim();

        // Already canonical
        if (k === "Open" || k === "MultipleChoice" || k === "TrueOrFalse") return k;

        // Type-name forms
        if (k === "OpenQuestion") return "Open";
        if (k === "MultipleChoiceQuestion") return "MultipleChoice";
        if (k === "TrueOrFalseQuestion") return "TrueOrFalse";

        // Human-readable forms
        if (k === "Multiple Choice") return "MultipleChoice";
        if (k === "True/False" || k === "True False") return "TrueOrFalse";

        return k;
    }

    async function fetchPartial(url) {
        const r = await fetch(url, { credentials: "same-origin" });

        const text = await r.text();

        if (!r.ok) {
            throw new Error(`fetchPartial failed (${r.status})`);
        }

        return text;
    }



    // ---------- custom modal ----------
    function ensureEditorModalStyles() {
        if (document.getElementById("editor-modal-styles")) return;

        const style = document.createElement("style");
        style.id = "editor-modal-styles";
        style.textContent = `
.editor-modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.62);
  z-index: 40000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.editor-modal {
  width: min(560px, 100%);
  background: #ffffff;
  border-radius: 14px;
  box-shadow: 0 24px 60px rgba(0, 0, 0, 0.35);
  overflow: hidden;
}

.editor-modal-header {
  padding: 16px 18px 10px 18px;
  border-bottom: 1px solid #e9ecef;
}

.editor-modal-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: #111827;
}

.editor-modal-body {
  padding: 14px 18px;
  color: #1f2937;
  line-height: 1.45;
}

.editor-modal-body ul {
  margin: 10px 0 0 18px;
  padding: 0;
}

.editor-modal-body li + li {
  margin-top: 6px;
}

.editor-modal-footer {
  padding: 12px 18px 16px 18px;
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  border-top: 1px solid #e9ecef;
}

.editor-modal-btn {
  border: 1px solid transparent;
  border-radius: 10px;
  padding: 8px 14px;
  font-weight: 600;
  font-size: 0.92rem;
  cursor: pointer;
}

.editor-modal-btn-secondary {
  background: #ffffff;
  color: #1f2937;
  border-color: #d1d5db;
}

.editor-modal-btn-primary {
  background: #1d4ed8;
  color: #ffffff;
}

.editor-modal-btn-danger {
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

    function showEditorModal(options) {
        ensureEditorModalStyles();

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
            overlay.className = "editor-modal-overlay";
            overlay.setAttribute("role", "presentation");

            const confirmClass = confirmVariant === "danger"
                ? "editor-modal-btn-danger"
                : "editor-modal-btn-primary";

            const detailList = details.length > 0
                ? `<ul>${details.map((x) => `<li>${escapeHtml(x)}</li>`).join("")}</ul>`
                : "";

            overlay.innerHTML = `
<div class="editor-modal" role="dialog" aria-modal="true" aria-labelledby="editorModalTitle">
  <div class="editor-modal-header">
    <h3 class="editor-modal-title" id="editorModalTitle">${escapeHtml(title)}</h3>
  </div>
  <div class="editor-modal-body">
    <div>${escapeHtml(message)}</div>
    ${detailList}
  </div>
  <div class="editor-modal-footer">
    ${hideCancel ? "" : `<button type="button" class="editor-modal-btn editor-modal-btn-secondary" data-action="cancel">${escapeHtml(cancelText)}</button>`}
    <button type="button" class="editor-modal-btn ${confirmClass}" data-action="confirm">${escapeHtml(confirmText)}</button>
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
    async function postForm(url, formData, signal, sourceForm) {
        const token = getAntiForgeryToken(sourceForm);

        // ASP.NET Core antiforgery supports either a header OR a form field (plus cookie).
        // To be extra robust, we send BOTH:
        // - RequestVerificationToken header
        // - __RequestVerificationToken field in the FormData (if not already present)
        const headers = {
            // IMPORTANT: must match server-side IsAjaxOrApiRequest() check
            "X-Requested-With": "XMLHttpRequest",
            // Most handlers here return JSON
            "Accept": "application/json"
        };

        if (token) {
            headers["RequestVerificationToken"] = token;

            // Avoid duplicating the field if the form already has it.
            if (formData && typeof formData.has === "function" && !formData.has("__RequestVerificationToken")) {
                formData.append("__RequestVerificationToken", token);
            }
        }

        return await fetch(url, {
            method: "POST",
            headers,
            body: formData,
            signal,
            credentials: "same-origin"
        });
    }

    // ---------- soft retry (network/5xx) ----------
    function isRetriableStatus(status) {
        // Retry transient failures only
        return status === 408 || status === 429 || status === 502 || status === 503 || status === 504 || (status >= 500 && status <= 599);
    }

    function sleep(ms, signal) {
        return new Promise((resolve, reject) => {
            if (signal?.aborted) return reject(Object.assign(new Error("Aborted"), { name: "AbortError" }));
            const t = setTimeout(resolve, ms);
            if (signal) {
                signal.addEventListener("abort", () => {
                    clearTimeout(t);
                    reject(Object.assign(new Error("Aborted"), { name: "AbortError" }));
                }, { once: true });
            }
        });
    }
    async function postFormWithRetry(url, formData, controller, seqAtStart, sourceForm) {
        const signal = controller?.signal;
        const delays = [0, 600, 1500, 3000];
        let lastErr = null;

        for (let attempt = 0; attempt < delays.length; attempt++) {
            if (seqAtStart !== saveSequence) return null;

            if (attempt > 0) {
                await sleep(delays[attempt], signal);
            }

            try {
                const res = await postForm(url, formData, signal, sourceForm);

                if (seqAtStart !== saveSequence) return null;

                if (!res.ok && isRetriableStatus(res.status) && attempt < delays.length - 1) {
                    lastErr = new Error(`Transient HTTP error (${res.status})`);
                    continue;
                }

                return res;
            } catch (err) {
                if (err?.name === "AbortError") throw err;

                lastErr = err;
                if (attempt >= delays.length - 1) throw err;
                continue;
            }
        }

        if (lastErr) throw lastErr;
        throw new Error("Request failed.");
    }

    // ---------- TinyMCE lifecycle ----------
    function destroyAllTinyMce() {
        if (window.tinymce && typeof window.tinymce.remove === "function") {
            // remove all instances (safe when swapping DOM)
            window.tinymce.remove();
        }
    }

    function initTinyMceForHost(queueSaveFn) {
        // Init editors for any textarea.tinymce currently in DOM
        if (!window.TinyMceHelpers) return;

        // Ensure validation plays nice (if you use jquery validate)
        TinyMceHelpers.configureJQueryValidationForTinyMce();

        if (window.ExamRichEditFields) {
            const queueAfterRichTextSync = () => {
                try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }
                queueSaveFn();
            };

            window.ExamRichEditFields.init(document, {
                height: 260,
                min_height: 160,
                onChange: queueAfterRichTextSync,
                onClose: queueAfterRichTextSync
            });
            return;
        }

        TinyMceHelpers.initEditors(
            ["textarea.tinymce"],
            {
                height: 260,
                setup: function (editor) {
                    // Avoid autosave firing during initial editor startup / content set.
                    let ready = false;
                    let queued = false;
                    const queueOnce = () => {
                        if (queued) return;
                        queued = true;
                        setTimeout(() => {
                            queued = false;
                            queueSaveFn();
                        }, 0);
                    };

                    editor.on("init", () => {
                        ready = true;
                        // Ensure editor starts clean
                        try { editor.setDirty(false); } catch { /* ignore */ }
                    });

                    // Primary signals: input/change cover typing and most edits.
                    // undo/redo needed because they can change content without a plain input event.
                    // paste/cut cover Ctrl+V / Ctrl+X and context-menu actions.
                    // setcontent is included for plugin-driven content inserts (still guarded by isDirty/ready).
                    const safeIsDirty = () => {
                        try { return !!editor.isDirty(); } catch { return null; }
                    };

                    const fireIfUserChangeSoon = (evtName) => {

                        if (!ready) {
                            return;
                        }

                        const dirtyNow = safeIsDirty();

                        setTimeout(() => {
                            if (!ready) return;

                            queueOnce();
                        }, 0);
                    };


                    ["input", "undo", "redo", "paste", "cut"].forEach(name => {
                        editor.on(name, () => fireIfUserChangeSoon(name));
                    });

                    // Safety net: Backspace/Delete can be noisy; only queue if TinyMCE reports dirty.
                    editor.on("keydown", (e) => {
                        if (e.key === "Backspace" || e.key === "Delete") {
                            setTimeout(() => {
                                try {
                                    if (!ready) return;
                                    if (!editor.isDirty()) return;
                                } catch {
                                    // If isDirty fails, fall back to queueing
                                }
                                try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }
                                queueOnce(); // IMPORTANT: use the per-editor coalescer
                            }, 0);
                        }
                    });

                    // On blur, always sync content back to textarea and autosave if dirty.
                    editor.on("blur", () => {

                        // Sync TinyMCE -> textarea (for validation / DOM consistency)
                        try { window.tinymce?.triggerSave?.(); } catch { }
                        queueOnce();
                    });

                }
            }
        );
    }

    // ---------- drafts ----------
    let nextTempId = -1;
    const drafts = new Map(); // tempId -> { kind, questionNumber }

    function getMaxQuestionNumberFromButtons() {
        let max = 0;

        document
            .querySelectorAll(".question-nav-btn[data-question-id]")
            .forEach(btn => {
                const n = Number(btn.textContent);
                if (!Number.isNaN(n)) max = Math.max(max, n);
            });

        return max;
    }

    function getBtnGroup() {
        const addBtn = document.querySelector(".question-add-btn")
            || document.querySelector('[data-action="add-question"]');

        return addBtn ? addBtn.closest(".question-button-group") : null;
    }


    function insertQuestionButton(questionId, questionNumber) {
        const group = getBtnGroup();
        if (!group) return;

        const addMenu = group.querySelector(".question-add-menu");

        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "question-nav-btn";
        btn.dataset.action = "load-question";
        btn.dataset.questionId = String(questionId);
        btn.textContent = String(questionNumber);

        if (addMenu) group.insertBefore(btn, addMenu);
        else group.appendChild(btn);
    }


    function setActiveQuestionButton(questionId) {
        document
            .querySelectorAll(".question-nav-btn[data-question-id]")
            .forEach((button) => {
                button.classList.remove("is-active");
                button.removeAttribute("aria-current");
            });

        const active = document.querySelector(
            `.question-nav-btn[data-question-id="${questionId}"]`
        );

        if (active) {
            active.classList.add("is-active");
            active.setAttribute("aria-current", "page");
        }
    }


    function replaceQuestionButtonId(oldId, newId) {
        const btn = document.querySelector(
            `.question-nav-btn[data-question-id="${oldId}"]`
        );
        if (!btn) return;

        btn.dataset.questionId = String(newId);
    }

    async function refreshPagination(currentQuestionId) {
        const paginationHost = document.getElementById("pagination-host");
        const examId = getExamId();
        if (!paginationHost || !examId) return false;

        const url =
            `${pagePath()}?handler=Pagination`
            + `&examId=${encodeURIComponent(examId)}`
            + `&currentQuestionId=${encodeURIComponent(currentQuestionId || 0)}`
            + `&_=${Date.now()}`;

        const res = await fetch(url, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            cache: "no-store",
            credentials: "same-origin"
        });

        if (!res.ok) return false;

        paginationHost.innerHTML = await res.text();
        if (currentQuestionId) setActiveQuestionButton(currentQuestionId);
        positionOpenQuestionAddMenu();
        return true;
    }

    function positionQuestionAddMenu(details) {
        if (!details?.open) return;

        const trigger = details.querySelector(".question-add-btn");
        const menu = details.querySelector(".question-add-menu-list");
        if (!trigger || !menu) return;

        const viewportPadding = 8;
        const rect = trigger.getBoundingClientRect();
        const menuWidth = Math.min(menu.offsetWidth || 208, window.innerWidth - viewportPadding * 2);
        const maxLeft = Math.max(viewportPadding, window.innerWidth - menuWidth - viewportPadding);
        const left = Math.min(Math.max(viewportPadding, rect.left), maxLeft);
        const bottom = Math.max(viewportPadding, window.innerHeight - rect.top + viewportPadding);

        menu.style.setProperty("--question-add-menu-left", `${left}px`);
        menu.style.setProperty("--question-add-menu-bottom", `${bottom}px`);
    }

    function positionOpenQuestionAddMenu() {
        document.querySelectorAll(".question-add-menu[open]").forEach(positionQuestionAddMenu);
    }

    let autosaveFailedForShellKey = null;

    function markAutosaveFailedForCurrentShell() {
        autosaveFailedForShellKey = getActiveShellKey() || "unknown";
    }

    function clearAutosaveFailureIfCurrentShell() {
        const currentShellKey = getActiveShellKey() || "unknown";
        if (!autosaveFailedForShellKey || autosaveFailedForShellKey === currentShellKey) {
            autosaveFailedForShellKey = null;
        }
    }

    async function confirmExitWhenAutosaveFailed(actionLabel) {
        if (!autosaveFailedForShellKey) return true;

        const currentShellKey = getActiveShellKey() || "unknown";
        if (autosaveFailedForShellKey !== currentShellKey) return true;

        return await showEditorModal({
            title: "Unsaved changes risk",
            message: `Autosave failed for this question. If you continue to ${actionLabel}, changes on this question may be lost.`,
            confirmText: "Continue anyway",
            cancelText: "Stay on this question",
            confirmVariant: "danger"
        });
    }
    function showSavedThenUpToDate(host, token, ms) {
        showStatus(host, "Saved", token);

        window.setTimeout(() => {
            if (token !== saveStatusToken) return;
            showStatus(host, "Up-to-date", token);
        }, ms ?? 2000);
    }

    function updateHeaderSaveStatusVisual(message) {
        const icon = document.getElementById("editorSaveIcon");
        const retryButton = document.getElementById("editorSaveRetry");
        if (!icon) return;

        const msg = String(message || "").toLowerCase();

        icon.classList.remove("fa-check", "fa-rotate", "fa-rotate-right", "fa-spin", "fa-pen", "fa-circle-exclamation");

        let retryEnabled = false;
        let retryTitle = "";

        if (msg.includes("save failed") || msg.includes("failed")) {
            icon.classList.add("fa-rotate-right");
            retryEnabled = true;
            retryTitle = "Retry save now";
        } else if (msg.includes("saving")) {
            icon.classList.add("fa-rotate", "fa-spin");
        } else if (msg.includes("unsaved")) {
            icon.classList.add("fa-pen");
        } else {
            icon.classList.add("fa-check");
        }

        if (retryButton) {
            retryButton.disabled = !retryEnabled;
            retryButton.title = retryTitle;
            retryButton.classList.toggle("exam-save-retry-danger", retryEnabled);
            retryButton.classList.toggle("exam-save-retry-muted", !retryEnabled);
        }
    }

    function showStatus(host, text, token) {
        // If a token is provided, only apply if it matches the latest token.
        if (typeof token === "number" && token !== saveStatusToken) return;

        const msg = text || "";

        // 1) Per-question status (keeps your existing behavior)
        const localEl = host?.querySelector?.("[data-save-status]");
        if (localEl) localEl.textContent = msg;

        // 2) Header status (new)
        const headerEl = document.getElementById("editorSaveStatus");
        if (!headerEl) return;

        headerEl.textContent = msg;
        updateHeaderSaveStatusVisual(msg);

        headerEl.classList.remove("exam-save-retry-danger");

        const t = msg.toLowerCase();
        if (t.includes("save failed")) {
            markAutosaveFailedForCurrentShell();
            headerEl.classList.add("exam-save-retry-danger");
            return;
        }

        if (t === "saved" || t === "up-to-date") {
            clearAutosaveFailureIfCurrentShell();
        }

        if (t.includes("failed")) headerEl.classList.add("exam-save-retry-danger");
    }

    let activeSavePromise = null;

    async function saveCurrentForm(statusToken, options) {
        while (activeSavePromise) {
            try {
                await activeSavePromise;
            } catch {
                // A fresh pass below retries the latest form state.
            }
        }

        const savePromise = performSaveCurrentForm(statusToken, options);
        activeSavePromise = savePromise;

        try {
            return await savePromise;
        } finally {
            if (activeSavePromise === savePromise) {
                activeSavePromise = null;
            }

            if (saveRequestedWhileSaving) {
                saveRequestedWhileSaving = false;
                window.setTimeout(() => {
                    if (isCurrentShellDirtyComparedToBaseline()) {
                        queueSave();
                    }
                }, 0);
            }
        }
    }

    async function performSaveCurrentForm(statusToken, options) {
        options = options || {};
        const silentStatus = !!options.silentStatus;

        const host = getQuestionHost();
        if (!host) throw new Error("Missing #questionHost.");

        const form = host.querySelector("form[data-question-edit-form]");
        if (!form) return null;

        const examId = getExamId();
        if (!examId) throw new Error("Missing ExamId.");

        // Identify current shell before any mutation
        const shellKeyBefore = getActiveShellKey();

        // Push TinyMCE content back into textareas before fingerprinting/FormData
        try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }

        // Skip-save check (payload fingerprint)
        const fpBefore = computeFormFingerprint(form);
        const lastFp = getLastSavedFingerprint(shellKeyBefore);
        if (fpBefore && lastFp && fpBefore === lastFp) {
            // Nothing changed since last save; don't flash "Saved"

            if (!silentStatus) showStatus(host, "Up-to-date", statusToken);
            return { success: true, skipped: true };
        }

        const controller = new AbortController();
        saveController = controller;

        // Monotonic sequence: only latest response may apply side effects
        const mySeq = ++saveSequence;

        const fd = new FormData(form);
        fd.append("examId", String(examId));

        // Strip all File fields from autosave payload.
        // Uploads are handled via presigned PUT + *Reference hidden inputs*.
        const fileKeys = [];
        for (const [k, v] of fd.entries()) {
            if (v instanceof File && v.size > 0) fileKeys.push(k);
        }
        for (const k of fileKeys) fd.delete(k);


        // Ensure non-null text for DB (server also handles, but keep client sane)
        if (!fd.has("text") && !fd.has("Text")) {
            fd.append("Text", "");
        }

        if (!silentStatus) showStatus(host, "Saving...", statusToken);

        let res;
        try {
            res = await postFormWithRetry(
                `${pagePath()}?handler=SaveQuestion`,
                fd,
                controller,
                mySeq,
                form // ? sourceForm
            );
        } catch (err) {
            // Abort is not an error state worth showing
            if (err?.name === "AbortError") return null;

            if (!silentStatus) showStatus(host, "Save failed", statusToken);
            throw err;
        } finally {
            if (saveController === controller) {
                saveController = null;
            }
        }

        // If a newer request started while this one was in flight, ignore this response
        if (mySeq !== saveSequence) return null;

        if (!res.ok) {
            const body = await res.text();
            if (!silentStatus) showStatus(host, "Save failed", statusToken);
            throw new Error(`Save failed (${res.status}). ${body}`);
        }

        const json = await res.json();
        if (!json?.success) {
            if (!silentStatus) showStatus(host, "Save failed", statusToken);
            throw new Error(json?.errorMessage || "Save failed.");
        }

        // If a newer request started after parsing JSON, ignore side effects
        if (mySeq !== saveSequence) return null;

        // If draft became real
        const returnedId = Number(json.questionId || 0);
        let persistedDraft = false;
        if (returnedId > 0) {
            const qidInput = form.querySelector('input[name="QuestionId"]');
            if (qidInput) qidInput.value = String(returnedId);

            const shell = host.querySelector("#question-shell");
            if (shell) {
                // Set persisted question id on the shell
                shell.dataset.questionId = String(returnedId);

                // Replace draft temp button id with real id (deterministic mapping)
                const tempId = shell.dataset.draftTempId ? Number(shell.dataset.draftTempId) : null;
                if (tempId && drafts.has(tempId)) {
                    drafts.delete(tempId);
                    persistedDraft = true;
                    replaceQuestionButtonId(tempId, returnedId);
                    setActiveQuestionButton(returnedId);
                }

                // Clear draft markers AFTER mapping
                if (shell.dataset.draft === "true") {
                    delete shell.dataset.draft;
                    delete shell.dataset.draftKind;
                    delete shell.dataset.draftTempId;
                }
            }
        }

        if (persistedDraft) {
            try {
                await refreshPagination(returnedId);
            } catch {
                // The temp button has already been reconciled locally; pagination can refresh on the next navigation.
            }
        }

        // Mark TinyMCE instances as clean after successful save
        try {
            if (window.tinymce?.editors?.length) {
                window.tinymce.editors.forEach(ed => {
                    let was = null, now = null;
                    try { was = !!ed.isDirty(); } catch { }
                    try { ed.setDirty(false); } catch { /* ignore */ }
                    try { now = !!ed.isDirty(); } catch { }
                });
            }
        } catch { /* ignore */ }

        // Update fingerprint baseline after successful save
        const shellKeyAfter = getActiveShellKey();
        try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }

        const fpAfter = computeFormFingerprint(form);

        if (fpAfter) {
            // If key changed (draft -> real), store new key baseline
            if (shellKeyBefore && shellKeyAfter && shellKeyBefore !== shellKeyAfter) {
                lastSavedFingerprintByShell.delete(shellKeyBefore);
            }
            setLastSavedFingerprint(shellKeyAfter || shellKeyBefore, fpAfter);
        }

        if (!silentStatus) showSavedThenUpToDate(host, statusToken, 2000);

        // If edits came in while we were saving, only re-queue if we're still dirty
        if (saveRequestedWhileSaving) {
            saveRequestedWhileSaving = false;

            setTimeout(() => {
                const dirty = isCurrentShellDirtyComparedToBaseline();

                if (dirty) {
                    queueSave();
                } else {
                }
            }, 0);
        }

        return json;
    }

    // ---------- autosave scoping (active shell lock) ----------
    function getActiveShellKey() {
        const host = getQuestionHost();
        if (!host) return null;

        const shell = host.querySelector("#question-shell");
        if (!shell) return null;

        // Prefer stable identifiers:
        // - Drafts are negative IDs (tempId)
        // - Persisted questions are positive IDs
        const qid = shell.dataset.questionId ?? "0";
        const isDraft = shell.dataset.draft === "true" ? "1" : "0";

        // Include kind for drafts as an extra discriminator
        const kind = shell.dataset.draftKind ?? "";

        return `${qid}|${isDraft}|${kind}`;
    }

    function cancelPendingAutosave() {
        if (debounceTimer) {
            clearTimeout(debounceTimer);
            debounceTimer = null;
        }
    }

    // ---------- save status state machine ----------
    let saveStatusToken = 0;

    function nextStatusToken() {
        saveStatusToken += 1;
        return saveStatusToken;
    }

    // ---------- in-flight save cancellation & sequencing ----------
    let saveController = null;
    let saveSequence = 0;

    function abortInFlightSave() {
        try { saveController?.abort?.(); } catch { /* ignore */ }
        saveController = null;
    }

    // ---------- payload fingerprinting (skip-save when unchanged) ----------
    const lastSavedFingerprintByShell = new Map();

    /**
     * Stable, small hash for strings (FNV-1a 32-bit).
     */
    function fnv1a32(str) {
        let h = 0x811c9dc5;
        for (let i = 0; i < str.length; i++) {
            h ^= str.charCodeAt(i);
            // h *= 16777619 (with 32-bit overflow)
            h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
        }
        return h.toString(16).padStart(8, "0");
    }

    /**
     * Creates a stable fingerprint for the current form payload.
     * - Sorts keys for determinism.
     * - Includes file metadata if any (name/size/type/lastModified) instead of the file contents.
     */
    function computeFormFingerprint(form) {
        if (!form) return null;

        const fd = new FormData(form);
        const pairs = [];

        for (const [key, val] of fd.entries()) {
            if (val instanceof File) {
                continue;
            } else {
                pairs.push([key, String(val)]);
            }
        }

        pairs.sort((a, b) => {
            if (a[0] !== b[0]) return a[0] < b[0] ? -1 : 1;
            if (a[1] !== b[1]) return a[1] < b[1] ? -1 : 1;
            return 0;
        });

        const canonical = pairs.map(([k, v]) => `${k}=${v}`).join("&");
        return fnv1a32(canonical);
    }

    /**
     * Returns the last saved fingerprint for the currently active shell.
     */
    function getLastSavedFingerprint(shellKey) {
        if (!shellKey) return null;
        return lastSavedFingerprintByShell.get(shellKey) ?? null;
    }

    /**
     * Stores the last saved fingerprint for a shell key.
     */
    function setLastSavedFingerprint(shellKey, fingerprint) {
        if (!shellKey || !fingerprint) return;
        lastSavedFingerprintByShell.set(shellKey, fingerprint);
    }

    function baselineCurrentShellAsSaved() {
        const host = getQuestionHost();
        if (!host) return;

        const form = host.querySelector("form[data-question-edit-form]");
        if (!form) return;

        // Delay one tick so TinyMCE can finish init/setcontent and triggerSave can actually copy values.
        setTimeout(() => {
            try { window.tinymce?.triggerSave?.(); } catch { }

            const shellKey = getActiveShellKey();
            if (!shellKey) return;

            const fp = computeFormFingerprint(form);
            if (!fp) return;

            setLastSavedFingerprint(shellKey, fp);

            const token = nextStatusToken();
            showStatus(host, "Up-to-date", token); // or whatever your neutral label is
        }, 0);
    }

    function hasInFlightSave() {
        if (activeSavePromise) return true;

        // If a controller exists and isn't aborted yet, consider it in-flight
        try {
            return !!saveController && saveController.signal && saveController.signal.aborted === false;
        } catch {
            return false;
        }
    }

    function isCurrentShellDirtyComparedToBaseline() {
        const host = getQuestionHost();
        if (!host) return false;

        const form = host.querySelector("form[data-question-edit-form]");
        if (!form) return false;

        // Sync TinyMCE before fingerprinting
        try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }

        const shellKey = getActiveShellKey();
        if (!shellKey) return false;

        const currentFp = computeFormFingerprint(form);
        const lastFp = getLastSavedFingerprint(shellKey);

        // If we don't have a baseline, be conservative: treat as dirty.
        if (!currentFp || !lastFp) return true;

        return currentFp !== lastFp;
    }

    function shouldWarnBeforeUnload() {
        // Warn if:
        // - there are unsaved edits, OR
        // - a debounced autosave is waiting, OR
        // - a save is currently in-flight
        return isCurrentShellDirtyComparedToBaseline() || debounceTimer !== null || hasInFlightSave();

    }

    // ---------- debouncer ----------
    let debounceTimer = null;
    let saveRequestedWhileSaving = false;
    let pendingToken = null;

    function queueSave() {
        const host = getQuestionHost();
        if (!host) return;

        const shellKeyAtQueueTime = getActiveShellKey();
        if (!shellKeyAtQueueTime) return;

        // If a save is already in-flight, do NOT advance the token.
        // Just mark that we need one more save pass after the current one finishes.
        if (hasInFlightSave()) {
            saveRequestedWhileSaving = true;
            return;
        }

        // Reuse one token for the whole debounce cycle (prevents token spam).
        const token = pendingToken ?? (pendingToken = nextStatusToken());
        showStatus(host, "Unsaved changes...", token);

        if (debounceTimer) clearTimeout(debounceTimer);

        debounceTimer = setTimeout(async () => {
            debounceTimer = null;

            // Consume the pending token for this save attempt.
            const t = pendingToken;
            pendingToken = null;

            // If user switched questions while waiting, do NOT save.
            const shellKeyNow = getActiveShellKey();
            if (!shellKeyNow || shellKeyNow !== shellKeyAtQueueTime) {
                return;
            }

            const form = host.querySelector("form[data-question-edit-form]");
            if (!form) return;

            // Ensure TinyMCE content is synced before fingerprinting
            try { window.tinymce?.triggerSave?.(); } catch { /* ignore */ }

            const fp = computeFormFingerprint(form);
            const lastFp = getLastSavedFingerprint(shellKeyAtQueueTime);



            // If payload is unchanged from the last successful save, do nothing.
            // Do NOT override the "Saved" dwell from the previous save.
            if (fp && lastFp && fp === lastFp) {
                return;
            }

            try {
                await saveCurrentForm(t); // saveCurrentForm owns Saving/Saved/Up-to-date
            } catch (e) {
                const shellKeyStill = getActiveShellKey();
                if (shellKeyStill === shellKeyAtQueueTime) {
                    showStatus(host, "Save failed", t);
                    notify(e?.message || "Failed to save changes.", { type: "error", title: "Save failed" });
                }
            }
        }, 800);
    }


    // ---------- actions ----------
    async function loadQuestion(questionId, options) {
        const host = getQuestionHost();
        if (!host) throw new Error("Missing #questionHost.");

        const skipExitGuard = !!options?.skipExitGuard;
        if (!skipExitGuard) {
            const canLeave = await confirmExitWhenAutosaveFailed("navigate to another question");
            if (!canLeave) return false;
        }

        cancelPendingAutosave();
        abortInFlightSave();
        nextStatusToken(); // invalidate any in-flight status updates

        // remove editors before swapping DOM
        destroyAllTinyMce();

        if (drafts.has(questionId)) {
            const examId = getExamId();
            if (!examId) throw new Error("Missing ExamId.");

            const d = drafts.get(questionId); // { kind, questionNumber }

            const url =
                `${pagePath()}?handler=QuestionShellDraft`
                + `&examId=${encodeURIComponent(examId)}`
                + `&kind=${encodeURIComponent(d.kind)}`
                + `&questionNumber=${encodeURIComponent(d.questionNumber)}`;

            const html = await fetchPartial(url);

            host.innerHTML = html;

            const shell = host.querySelector("#question-shell");
            if (shell) {
                shell.dataset.questionId = "0";
                shell.dataset.draft = "true";
                shell.dataset.draftKind = d.kind;
                shell.dataset.draftTempId = String(questionId); // important: this draft is keyed by the tempId you clicked
            }

            setActiveQuestionButton(questionId);

            initTinyMceForHost(queueSave);

            // Baseline after editors exist
            baselineCurrentShellAsSaved();
            return;
        }

        const examId = getExamId();
        if (!examId) throw new Error("Missing ExamId.");

        const url = `${pagePath()}?handler=QuestionShell&examId=${encodeURIComponent(examId)}&questionId=${encodeURIComponent(questionId)}`;
        const html = await fetchPartial(url);

        host.innerHTML = html;
        setActiveQuestionButton(questionId);

        initTinyMceForHost(queueSave);

        // Baseline after editors exist
        baselineCurrentShellAsSaved();

        try {
            const u = new URL(window.location.href);
            u.searchParams.set("questionId", String(questionId));
            history.replaceState({}, "", u.toString());
        } catch { /* ignore */ }
    }

    async function addDraft(kind, options) {
        // Prevent multiple drafts at once
        if (drafts.size > 0) {
            notify("You already have an unsaved draft question. Save or discard it before adding another.", { type: "warn", title: "Draft exists" });
            return;
        }

        kind = normalizeKind(kind);

        const host = getQuestionHost();
        if (!host) throw new Error("Missing #questionHost.");

        const skipExitGuard = !!options?.skipExitGuard;
        if (!skipExitGuard) {
            const canLeave = await confirmExitWhenAutosaveFailed("create a new question");
            if (!canLeave) return false;
        }

        cancelPendingAutosave();
        abortInFlightSave();
        nextStatusToken(); // invalidate any in-flight status updates

        destroyAllTinyMce();

        const examId = getExamId();
        if (!examId) throw new Error("Missing ExamId.");

        const questionNumber = getMaxQuestionNumberFromButtons() + 1;
        const tempId = nextTempId--;

        drafts.set(tempId, { kind, questionNumber });
        insertQuestionButton(tempId, questionNumber);
        setActiveQuestionButton(tempId);

        const url =
            `${pagePath()}?handler=QuestionShellDraft`
            + `&examId=${encodeURIComponent(examId)}`
            + `&kind=${encodeURIComponent(kind)}`
            + `&questionNumber=${encodeURIComponent(questionNumber)}`;

        const html = await fetchPartial(url);

        host.innerHTML = html;

        // mark shell as draft + store mapping to temp button id
        const shell = host.querySelector("#question-shell");
        if (shell) {
            shell.dataset.questionId = "0";
            shell.dataset.draft = "true";
            shell.dataset.draftKind = kind;
            shell.dataset.draftTempId = String(tempId);
        }

        initTinyMceForHost(queueSave);

        // Baseline after editors exist
        baselineCurrentShellAsSaved();
    }

    // ---------- event wiring ----------
    document.addEventListener("click", async (e) => {
        const retryBtn = e.target.closest("#editorSaveRetry");
        if (retryBtn) {
            e.preventDefault();
            if (retryBtn.disabled) return;
            await saveExam();
            return;
        }

        const quick = e.target.closest(".js-add-draft[data-question-type]");
        if (quick) {
            quick.closest(".question-add-menu")?.removeAttribute("open");
            await addDraft(quick.dataset.questionType);
            return;
        }

        // DELETE must be handled BEFORE the data-action early-return
        const delBtn = e.target.closest("#deleteQuestionBtn");
        if (delBtn) {
            const confirmedDelete = await showEditorModal({
                title: "Delete question",
                message: "Delete this question? This cannot be undone.",
                confirmText: "Delete question",
                cancelText: "Cancel",
                confirmVariant: "danger"
            });
            if (!confirmedDelete) return;

            const examId = getExamId();
            const questionId = getCurrentQuestionIdFromForm();

            if (!examId || !(questionId > 0)) {
                notify("You can only delete a saved question.", { type: "warn", title: "Not saved yet" });
                return;
            }

            const host = getQuestionHost();
            const token = nextStatusToken();
            showStatus(host, "Saving...", token);

            try {
                const form = host?.querySelector?.("form[data-question-edit-form]") ?? null;
                const fd = new FormData();
                fd.append("examId", String(examId));
                fd.append("questionId", String(questionId));

                const res = await postForm(`${pagePath()}?handler=DeleteQuestion`, fd, undefined, form);
                const json = await res.json().catch(async () => ({ success: false, errorMessage: await res.text() }));

                if (!res.ok || !json?.success) {
                    notify(json?.errorMessage || "Failed to delete question.", { type: "error", title: "Delete failed" });
                    showStatus(host, "Save failed", token);
                    return;
                }

                // Success
                notify("Question deleted.", { type: "success", title: "Deleted" });

                // Ensure status completes (prevents “Saving…” sticking)
                showSavedThenUpToDate(host, token, 2000);

                // Refresh pagination partial from server (source of truth)
                const paginationHost = document.getElementById("pagination-host");
                if (paginationHost) {
                    const url = `${pagePath()}?handler=Pagination&examId=${examId}&currentQuestionId=${encodeURIComponent(json.nextQuestionId || 0)}&_=${Date.now()}`;
                    const r = await fetch(url, {
                        headers: { "X-Requested-With": "XMLHttpRequest" },
                        cache: "no-store"
                    });

                    if (r.ok) {
                        paginationHost.innerHTML = await r.text();
                    }
                }

                // Load next question shell (this also updates ?questionId=)
                const nextId = Number(json?.nextQuestionId || 0);
                if (nextId > 0) {
                    await loadQuestion(nextId, { skipExitGuard: true });
                } else {
                    // No questions left: clear shell + remove questionId from URL
                    host.innerHTML = "";

                    try {
                        const u = new URL(window.location.href);
                        u.searchParams.delete("questionId");
                        history.replaceState({}, "", u.toString());
                    } catch { /* ignore */ }
                }
            } catch {
                notify("Failed to delete question.", { type: "error", title: "Delete failed" });
                showStatus(host, "Save failed", token);
            }

            return;
        }

        const actionEl = e.target.closest("[data-action]");
        if (!actionEl) return;

        try {
            const action = actionEl.dataset.action;

            if (action === "load-question") {
                const qid = Number(actionEl.dataset.questionId);
                if (Number.isFinite(qid)) await loadQuestion(qid);
                return;
            }
        } catch (err) {
            notify(err?.message || "Unexpected error.", { type: "error", title: "Action failed" });
        }
    });

    document.addEventListener("toggle", (e) => {
        const details = e.target;
        if (!details?.matches?.(".question-add-menu")) return;

        if (details.open) {
            document.querySelectorAll(".question-add-menu[open]").forEach((other) => {
                if (other !== details) other.removeAttribute("open");
            });
            positionQuestionAddMenu(details);
        }
    }, true);

    window.addEventListener("resize", positionOpenQuestionAddMenu);
    window.addEventListener("scroll", positionOpenQuestionAddMenu, true);


    // Autosave: regular inputs (non-tinymce) still work
    document.addEventListener("input", (e) => {
        const host = getQuestionHost();
        if (!host || !host.contains(e.target)) return;

        // TinyMCE changes are handled via editor events, so ignore textarea inputs
        // to avoid double-triggering.
        if (e.target && e.target.matches && e.target.matches("textarea.tinymce")) return;

        if (e.target.closest("form[data-question-edit-form]")) queueSave();
    }, true);

    document.addEventListener("change", (e) => {
        const host = getQuestionHost();
        if (!host || !host.contains(e.target)) return;

        if (e.target && (e.target.id === "imageInput" || e.target.id === "audioInput")) return;

        if (e.target && e.target.matches && e.target.matches("textarea")) return;

        if (e.target && e.target.matches && !e.target.matches('select, input[type="radio"], input[type="checkbox"]')) return;

        if (e.target.closest("form[data-question-edit-form]")) queueSave();
    }, true);

    document.addEventListener("DOMContentLoaded", async () => {
        // Init editors for any textareas already on the page
        initTinyMceForHost(queueSave);

        const examId = getExamId();
        if (!examId) return;

        const u = new URL(window.location.href);
        const qid = Number(u.searchParams.get("questionId") || "0");

        if (qid > 0) {
            await loadQuestion(qid); // loadQuestion() will baseline
            return;
        }

        // If the page already contains an initial shell (server-rendered), baseline it
        baselineCurrentShellAsSaved();
    });

    function uploadsEnabled() {
        const el = document.getElementById("UploadsEnabled");
        if (!el) return true;
        return (el.value || "").trim().toLowerCase() === "true";
    }

    function notifyUploadsDisabled() {
        notify("File uploads are disabled in this environment.", { type: "info", title: "Uploads unavailable" });
    }

    document.addEventListener("click", (e) => {
        if (uploadsEnabled()) return;

        const blocked = e.target.closest("#imageUploadBox, #imagePreview, #changeImageBtn, #audioUploadBox, #changeAudioBtn");
        if (!blocked) return;

        e.preventDefault();
        e.stopImmediatePropagation();
        notifyUploadsDisabled();
    }, true);

    document.addEventListener("change", (e) => {
        if (uploadsEnabled()) return;

        if (e.target.id === "imageInput") {
            resetImageUi(getImageUiContainerFrom(e.target));
            e.stopImmediatePropagation();
            notifyUploadsDisabled();
            return;
        }

        if (e.target.id === "audioInput") {
            resetAudioUi(getAudioUiContainerFrom(e.target));
            e.stopImmediatePropagation();
            notifyUploadsDisabled();
        }
    }, true);

    // ---------- image upload: delegated wiring + validation + replace ----------
    const IMAGE_MAX_BYTES = 5 * 1024 * 1024; // 5MB
    const IMAGE_ALLOWED_TYPES = new Set([
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    ]);

    function getImageUiContainerFrom(el) {
        return el.closest("[data-image-ui]");
    }

    function resetImageUi(container) {
        const uploadBox = container?.querySelector("#imageUploadBox");
        const preview = container?.querySelector("#imagePreview");
        const input = container?.querySelector("#imageInput");
        const changeBtn = container?.querySelector("#changeImageBtn");

        if (preview) {
            preview.removeAttribute("src");
            preview.classList.add("exam-hidden");
        }
        if (uploadBox) {
            uploadBox.classList.remove("exam-hidden");
        }
        if (changeBtn) {
            changeBtn.classList.add("exam-hidden");
        }
        if (input) {
            // Clear invalid/old selection
            input.value = "";
        }
    }

    function showPreview(container, file) {
        const uploadBox = container?.querySelector("#imageUploadBox");
        const preview = container?.querySelector("#imagePreview");
        const changeBtn = container?.querySelector("#changeImageBtn");
        const removeBtn = container?.querySelector("#removeImageBtn");

        if (!preview || !uploadBox) return;

        const url = URL.createObjectURL(file);
        preview.onload = () => URL.revokeObjectURL(url);
        preview.src = url;

        uploadBox.classList.add("exam-hidden");
        preview.classList.remove("exam-hidden");

        if (changeBtn) changeBtn.classList.remove("exam-hidden");
        if (removeBtn) removeBtn.classList.remove("exam-hidden"); // <-- IMPORTANT
    }


    function validateImageFile(file) {
        if (!file) return "No file selected.";
        if (!IMAGE_ALLOWED_TYPES.has(file.type)) {
            return "Invalid file type. Please upload a JPG, PNG, GIF, or WebP image.";
        }
        if (file.size > IMAGE_MAX_BYTES) {
            return "Image is too large. Maximum size is 5MB.";
        }
        return null;
    }

    function getCurrentQuestionIdFromForm() {
        const host = getQuestionHost();
        const form = host?.querySelector("form[data-question-edit-form]");
        if (!form) return 0;
        return Number(form.querySelector('input[name="QuestionId"]')?.value || "0");
    }

    async function ensureQuestionPersisted() {
        const qid = getCurrentQuestionIdFromForm();
        if (qid > 0) return qid;

        // Create the question immediately, but don't let this overwrite a global "Saving..." status
        const token = nextStatusToken(); // or reuse an existing operation token if you have one
        const saved = await saveCurrentForm(token, { silentStatus: true });
        const newId = Number(saved?.questionId || 0);
        if (newId <= 0) throw new Error("Could not create/save the question before upload.");
        return newId;
    }

    async function requestPresignedUpload(examId, questionId, file) {
        const fd = new FormData();
        fd.append("examId", String(examId));
        fd.append("questionId", String(questionId));
        fd.append("fileName", file.name);
        fd.append("contentType", file.type || "");

        const host = getQuestionHost();
        const sourceForm = host?.querySelector?.("form[data-question-edit-form]") ?? null;

        const res = await postForm(`${pagePath()}?handler=CreateImageUpload`, fd, undefined, sourceForm);

        // Try JSON first (your handler should always return JSON)
        let json = null;
        try {
            json = await res.clone().json();
        } catch {
            // non-JSON response (e.g., dev exception page HTML)
        }

        // If server gave structured failure, keep it small + coded
        if (json && json.success === false) {
            const code = json.errorCode || "PRESIGN_FAILED";
            const msg = json.errorMessage || "Upload initialization failed.";
            throw new Error(`Error ${code}. ${msg}`);
        }

        // If HTTP failed and we didn't get a structured JSON payload
        if (!res.ok) {
            throw new Error(`Error PRESIGN_FAILED. Upload initialization failed (HTTP ${res.status}).`);
        }

        // Success path
        if (!json?.success || !json?.putUrl || !json?.objectKey) {
            throw new Error("Error PRESIGN_FAILED. Upload initialization failed (invalid response).");
        }

        return json; // { putUrl, objectKey }
    }

    async function putFileToPresignedUrl(putUrl, file) {
        const res = await fetch(putUrl, {
            method: "PUT",
            headers: {
                "Content-Type": file.type || "application/octet-stream"
            },
            body: file
        });

        if (!res.ok) {
            const body = await res.text().catch(() => "");
            throw new Error(`PUT to object storage failed (${res.status}). ${body}`);
        }
    }

    function setHiddenReference(name, value) {
        const host = getQuestionHost();
        const form = host?.querySelector("form[data-question-edit-form]");
        if (!form) return false;

        const input =
            form.querySelector(`input[name="${name}"]`)
            || form.querySelector(`input[name="${name.toLowerCase()}"]`);

        if (!input) return false;
        input.value = value || "";
        return true;
    }
    function toggleEl(el, show) {
        if (!el) return;
        el.classList.toggle("exam-hidden", !show);
    }

    function clearAndHideImageUI() {
        const imageUploadBox = document.getElementById("imageUploadBox");
        const imagePreview = document.getElementById("imagePreview");
        const changeBtn = document.getElementById("changeImageBtn");
        const removeBtn = document.getElementById("removeImageBtn");
        const imageInput = document.getElementById("imageInput");

        if (imagePreview) { imagePreview.src = ""; toggleEl(imagePreview, false); }
        toggleEl(imageUploadBox, true);
        toggleEl(changeBtn, false);
        toggleEl(removeBtn, false);
        if (imageInput) imageInput.value = ""; // clear any selected file
    }

    function clearAndHideAudioUI() {
        const audioUploadBox = document.getElementById("audioUploadBox");
        const audioPreview = document.getElementById("audioPreview");
        const changeBtn = document.getElementById("changeAudioBtn");
        const removeBtn = document.getElementById("removeAudioBtn");
        const audioInput = document.getElementById("audioInput");

        if (audioPreview) {
            audioPreview.pause?.();
            audioPreview.removeAttribute("src");
            audioPreview.load?.();
            toggleEl(audioPreview, false);
        }
        toggleEl(audioUploadBox, true);
        toggleEl(changeBtn, false);
        toggleEl(removeBtn, false);
        if (audioInput) audioInput.value = "";
    }


    // Click upload box => open picker
    document.addEventListener("click", (e) => {
        const uploadBox = e.target.closest("#imageUploadBox");
        if (!uploadBox) return;

        const container = getImageUiContainerFrom(uploadBox);
        const input = container?.querySelector("#imageInput");
        if (input) input.click();
    });

    // Optional: Click preview => change image
    document.addEventListener("click", (e) => {
        const preview = e.target.closest("#imagePreview");
        if (!preview || preview.classList.contains("exam-hidden")) return;

        const container = getImageUiContainerFrom(preview);
        const input = container?.querySelector("#imageInput");
        if (input) input.click();
    });

    // Optional: "Change image" button => open picker
    document.addEventListener("click", (e) => {
        const btn = e.target.closest("#changeImageBtn");
        if (!btn) return;

        const container = getImageUiContainerFrom(btn);
        const input = container?.querySelector("#imageInput");
        if (input) input.click();
    });

    document.addEventListener("change", async (e) => {
        if (e.target.id !== "imageInput") return;

        const input = e.target;
        const container = getImageUiContainerFrom(input);
        if (!container) return;

        const file = input.files && input.files[0];

        const validationError = validateImageFile(file);
        if (validationError) {
            resetImageUi(container);
            return;
        }

        showPreview(container, file);

        let token = null;

        try {
            const host = getQuestionHost();
            token = nextStatusToken();
            showStatus(host, "Saving...", token);

            const examId = getExamId();
            if (!examId) throw new Error("Missing ExamId.");

            // Ensure we have a real QuestionId (server handler requires it)
            const questionId = await ensureQuestionPersisted();

            // Ask server for presigned PUT URL
            const { putUrl, objectKey } = await requestPresignedUpload(examId, questionId, file);

            // Upload file directly to object storage
            await putFileToPresignedUrl(putUrl, file);

            // Store reference for SaveQuestion
            if (!setHiddenReference("ImageReference", objectKey)) {
                setHiddenReference("imageReference", objectKey);
            }

            // Clear file input so it does not keep affecting fingerprint/autosave
            input.value = "";

            // Persist reference NOW
            await saveCurrentForm(token, { silentStatus: true });

            showSavedThenUpToDate(host, token, 2000);
        } catch (err) {
            notify(
                err?.message || "Image upload failed.",
                { type: "error", title: "Upload failed" }
            );

            const host = getQuestionHost();
            if (host) showStatus(host, "Save failed", token ?? nextStatusToken());
        }
    });

    document.addEventListener("click", async (e) => {
        const imgBtn = e.target.closest("#removeImageBtn");
        if (imgBtn) {
            const confirmedRemoveImage = await showEditorModal({
                title: "Remove image",
                message: "Remove the image from this question?",
                confirmText: "Remove image",
                cancelText: "Cancel",
                confirmVariant: "danger"
            });
            if (!confirmedRemoveImage) return;

            const host = getQuestionHost?.() || document.getElementById("questionHost") || document;
            const form = host.querySelector?.("form[data-question-edit-form]");
            if (!form) return;

            const examId = getExamId();
            const questionId = getCurrentQuestionIdFromForm();
            if (!examId || !(questionId > 0)) return;

            const token = nextStatusToken();
            showStatus(host, "Saving...", token);

            try {
                const fd = new FormData();
                fd.append("examId", String(examId));
                fd.append("questionId", String(questionId));
                fd.append("kind", "image");

                const res = await postForm(`${pagePath()}?handler=DeleteQuestionFile`, fd, undefined, form);

                const json = await res.json().catch(async () => ({ success: false, errorMessage: await res.text() }));

                if (!res.ok || !json?.success) throw new Error(json?.errorMessage || `Delete failed (${res.status}).`);

                setHiddenReference("ImageReference", "");
                clearAndHideImageUI();

                await saveCurrentForm(token, { silentStatus: true });
                showSavedThenUpToDate(host, token, 2000);
            } catch (err) {
                notify(
                    err?.message || "Failed to remove the file.",
                    { type: "error", title: "Remove failed" }
                );
                showStatus(host, "Save failed", token);
            }

            return;
        }

        const audBtn = e.target.closest("#removeAudioBtn");
        if (audBtn) {
            const confirmedRemoveAudio = await showEditorModal({
                title: "Remove audio",
                message: "Remove the audio from this question?",
                confirmText: "Remove audio",
                cancelText: "Cancel",
                confirmVariant: "danger"
            });
            if (!confirmedRemoveAudio) return;

            const host = getQuestionHost?.() || document.getElementById("questionHost") || document;
            const form = host.querySelector?.("form[data-question-edit-form]");
            if (!form) return;

            const examId = getExamId();
            const questionId = getCurrentQuestionIdFromForm();
            if (!examId || !(questionId > 0)) return;

            const token = nextStatusToken();
            showStatus(host, "Saving...", token);

            try {
                const fd = new FormData();
                fd.append("examId", String(examId));
                fd.append("questionId", String(questionId));
                fd.append("kind", "audio");

                const res = await postForm(`${pagePath()}?handler=DeleteQuestionFile`, fd, undefined, form);

                const json = await res.json().catch(async () => ({ success: false, errorMessage: await res.text() }));

                if (!res.ok || !json?.success) throw new Error(json?.errorMessage || `Delete failed (${res.status}).`);

                setHiddenReference("AudioReference", "");
                clearAndHideAudioUI();

                await saveCurrentForm(token, { silentStatus: true });
                showSavedThenUpToDate(host, token, 2000);
            } catch (err) {
                notify(
                    err?.message || "Failed to remove the file.",
                    { type: "error", title: "Remove failed" }
                );
                showStatus(host, "Save failed", token);
            }
        }
    });


    // ---------- audio upload: delegated wiring + validation + preview ----------
    const AUDIO_MAX_BYTES = 15 * 1024 * 1024; // 15MB (adjust as you want)
    const AUDIO_ALLOWED_TYPES = new Set([
        "audio/mpeg",
        "audio/mp3",     // some browsers/clients report mp3 like this
        "audio/mp4",
        "audio/x-m4a",   // keep only if also allowed server-side
        "audio/aac",
        "audio/wav",
        "audio/ogg",
        "audio/webm"
    ]);


    function getAudioUiContainerFrom(el) {
        return el.closest("[data-audio-ui]");
    }

    function resetAudioUi(container) {
        const uploadBox = container?.querySelector("#audioUploadBox");
        const input = container?.querySelector("#audioInput");
        const player = container?.querySelector("#audioPreview");     // <audio id="audioPreview" controls>
        const changeBtn = container?.querySelector("#changeAudioBtn");

        if (player) {
            player.removeAttribute("src");
            player.classList.add("exam-hidden");
            try { player.load(); } catch { /* ignore */ }
        }

        if (uploadBox) uploadBox.classList.remove("exam-hidden");
        if (changeBtn) changeBtn.classList.add("exam-hidden");

        if (input) input.value = "";
    }

    function showAudioPreview(container, file) {
        const uploadBox = container?.querySelector("#audioUploadBox");
        const player = container?.querySelector("#audioPreview");
        const changeBtn = container?.querySelector("#changeAudioBtn");
        const removeBtn = container?.querySelector("#removeAudioBtn");

        if (!player || !uploadBox) return;

        const url = URL.createObjectURL(file);
        player.onloadedmetadata = () => URL.revokeObjectURL(url);
        player.src = url;

        uploadBox.classList.add("exam-hidden");
        player.classList.remove("exam-hidden");

        if (changeBtn) changeBtn.classList.remove("exam-hidden");
        if (removeBtn) removeBtn.classList.remove("exam-hidden"); // <-- IMPORTANT
    }

    function validateAudioFile(file) {
        if (!file) return "No file selected.";
        if (!AUDIO_ALLOWED_TYPES.has(file.type)) {
            return "Invalid file type. Please upload MP3, M4A, WAV, OGG, or WebM audio.";
        }
        if (file.size > AUDIO_MAX_BYTES) {
            return "Audio is too large. Maximum size exceeded.";
        }
        return null;
    }

    // Click upload box => open picker
    document.addEventListener("click", (e) => {
        const uploadBox = e.target.closest("#audioUploadBox");
        if (!uploadBox) return;

        const container = getAudioUiContainerFrom(uploadBox);
        const input = container?.querySelector("#audioInput");
        if (input) input.click();
    });

    // Optional: Click "Change audio" button => open picker
    document.addEventListener("click", (e) => {
        const btn = e.target.closest("#changeAudioBtn");
        if (!btn) return;

        const container = getAudioUiContainerFrom(btn);
        const input = container?.querySelector("#audioInput");
        if (input) input.click();
    });

    // When file selected => validate => preview (or reject)
    document.addEventListener("change", (e) => {
        if (e.target.id !== "audioInput") return;

        const input = e.target;
        const container = getAudioUiContainerFrom(input);
        if (!container) return;

        const file = input.files && input.files[0];

        const validationError = validateAudioFile(file);
        if (validationError) {
            resetAudioUi(container);
            return;
        }

        showAudioPreview(container, file);

        (async () => {
            let token = null;
            let host = null;

            try {
                host = getQuestionHost();
                if (!host) return;
                const form = host?.querySelector("form[data-question-edit-form]");

                token = nextStatusToken();
                showStatus(host, "Saving...", token);

                const examId = getExamId();
                if (!examId) throw new Error("Missing ExamId.");

                // Create question first if needed (server requires questionId > 0)
                const questionId = await ensureQuestionPersisted();

                // Get presigned PUT URL
                const { putUrl, objectKey } = await requestPresignedUpload(examId, questionId, file);

                // Upload directly to object storage
                await putFileToPresignedUrl(putUrl, file);

                // Write the reference into the hidden input so SaveQuestion persists it
                if (!setHiddenReference("AudioReference", objectKey)) {
                    setHiddenReference("audioReference", objectKey); // fallback
                }

                // Clear the file input so it doesn't keep influencing autosave/fingerprint
                input.value = "";

                // Persist reference NOW
                await saveCurrentForm(token, { silentStatus: true });

                showSavedThenUpToDate(host, token, 2000);
            } catch (err) {
                notify(
                    err?.message || "Audio upload failed.",
                    { type: "error", title: "Upload failed" }
                );

                host = host || getQuestionHost();
                if (host) {
                    showStatus(host, "Save failed", token ?? nextStatusToken());
                }
            }
        })();

    });


    function toIssueDetails(issues) {
        if (!Array.isArray(issues) || issues.length === 0) return [];

        return issues
            .map((item) => {
                if (!item) return null;

                if (typeof item === "string") return item;

                if (typeof item.message === "string" && item.message.trim().length > 0) {
                    return item.message;
                }

                return null;
            })
            .filter((x) => !!x);
    }

    function getFirstIssueQuestionId(issues) {
        if (!Array.isArray(issues) || issues.length === 0) return 0;

        for (const item of issues) {
            const qid = Number(item?.questionId || 0);
            if (Number.isFinite(qid) && qid > 0) {
                return qid;
            }
        }

        return 0;
    }

    function getFirstIssueQuestionNumber(issues) {
        if (!Array.isArray(issues) || issues.length === 0) return 0;

        for (const item of issues) {
            const qn = Number(item?.questionNumber || 0);
            if (Number.isFinite(qn) && qn > 0) {
                return qn;
            }
        }

        return 0;
    }


    let publishInProgress = false;
    let unpublishInProgress = false;

    function renderPublishedStateUi() {
        const publishBtn = document.getElementById("publishExamBtn");
        const status = document.querySelector("[data-publish-status]");
        if (!publishBtn) return;

        const isPublished = publishBtn.dataset.published === "true";
        const loadingAction = publishInProgress
            ? "publish"
            : (unpublishInProgress ? "unpublish" : "");
        const isLoading = loadingAction.length > 0;

        publishBtn.classList.toggle("is-published", isPublished);
        publishBtn.classList.toggle("is-draft", !isPublished);
        publishBtn.disabled = isLoading;
        publishBtn.setAttribute("aria-label", isPublished ? "Unpublish exam" : "Publish exam");

        const publishIcon = publishBtn.querySelector("[data-publish-icon]");
        const unpublishIcon = publishBtn.querySelector("[data-unpublish-icon]");
        const spinner = publishBtn.querySelector("[data-publish-spinner]");
        const label = publishBtn.querySelector("[data-publish-label]");

        if (publishIcon) publishIcon.classList.toggle("exam-hidden", isLoading || isPublished);
        if (unpublishIcon) unpublishIcon.classList.toggle("exam-hidden", isLoading || !isPublished);
        if (spinner) spinner.classList.toggle("exam-hidden", !isLoading);

        if (label) {
            label.textContent = loadingAction === "publish"
                ? "Publishing..."
                : loadingAction === "unpublish"
                    ? "Unpublishing..."
                    : isPublished ? "Unpublish" : "Publish";
        }

        if (status) {
            status.classList.toggle("is-published", isPublished);
            status.classList.toggle("is-draft", !isPublished);

            const statusLabel = status.querySelector("[data-publish-status-label]");
            if (statusLabel) statusLabel.textContent = isPublished ? "Published" : "Not published";
        }
    }

    function setPublishedStateUi(isPublished) {
        const publishBtn = document.getElementById("publishExamBtn");
        if (!publishBtn) return;

        publishBtn.dataset.published = isPublished ? "true" : "false";
        renderPublishedStateUi();
    }

    function setPublishButtonLoading() {
        renderPublishedStateUi();
    }

    function setUnpublishButtonLoading() {
        renderPublishedStateUi();
    }

    async function postPublishExam(examId, publishWithWarnings) {
        const host = getQuestionHost();
        const form = host?.querySelector?.("form[data-question-edit-form]") ?? null;

        const fd = new FormData();
        fd.append("examId", String(examId));
        fd.append("publishWithWarnings", publishWithWarnings ? "true" : "false");

        const res = await postForm(`${pagePath()}?handler=PublishExam`, fd, undefined, form);

        let json = null;
        try {
            json = await res.clone().json();
        } catch {
            json = null;
        }

        if (!res.ok) {
            const body = json?.errorMessage || (await res.text().catch(() => "")) || `Publish request failed (${res.status}).`;
            throw new Error(body);
        }

        return json || {};
    }


    async function postUnpublishExam(examId) {
        const host = getQuestionHost();
        const form = host?.querySelector?.("form[data-question-edit-form]") ?? null;

        const fd = new FormData();
        fd.append("examId", String(examId));

        const res = await postForm(`${pagePath()}?handler=UnpublishExam`, fd, undefined, form);

        let json = null;
        try {
            json = await res.clone().json();
        } catch {
            json = null;
        }

        if (!res.ok) {
            const body = json?.errorMessage || (await res.text().catch(() => "")) || `Unpublish request failed (${res.status}).`;
            throw new Error(body);
        }

        return json || {};
    }

    async function saveExam() {
        const host = getQuestionHost();
        const form = host?.querySelector?.("form[data-question-edit-form]");

        if (!host || !form) {
            notify("No editable question is open.", { type: "warn", title: "Nothing to save" });
            return;
        }

        const token = nextStatusToken();

        try {
            await saveCurrentForm(token);
            notify("Current question saved.", { type: "success", title: "Saved" });
        } catch (err) {
            showStatus(host, "Save failed", token);
            notify(err?.message || "Failed to save current question.", { type: "error", title: "Save failed" });
        }
    }

    async function toggleExamPublishState() {
        if (publishInProgress || unpublishInProgress) return;

        const publishBtn = document.getElementById("publishExamBtn");
        if (!publishBtn) return;

        if (publishBtn.dataset.published === "true") {
            await unpublishExam();
            return;
        }

        await publishExam();
    }

    async function publishExam() {
        if (publishInProgress) return;

        const examId = getExamId();
        if (!examId) {
            notify("Missing exam id.", { type: "error", title: "Publish failed" });
            return;
        }

        publishInProgress = true;
        setPublishButtonLoading(true);

        try {
            const canLeaveAfterFailure = await confirmExitWhenAutosaveFailed("publish the exam");
            if (!canLeaveAfterFailure) return;

            try {
                await window.examSettingsCanvas?.saveIfDirty?.();
            } catch (err) {
                notify(err?.message || "Settings could not be saved before publishing.", { type: "error", title: "Publish stopped" });
                return;
            }

            const host = getQuestionHost();
            const form = host?.querySelector?.("form[data-question-edit-form]") ?? null;

            if (host && form) {
                const saveToken = nextStatusToken();

                try {
                    await saveCurrentForm(saveToken);
                } catch (err) {
                    const continueAfterSaveFailure = await showEditorModal({
                        title: "Autosave failed",
                        message: "Latest edits could not be saved. If you publish now, those changes may not be included.",
                        confirmText: "Publish anyway",
                        cancelText: "Go back",
                        confirmVariant: "danger"
                    });

                    if (!continueAfterSaveFailure) return;
                }
            }

            let firstPass;
            try {
                firstPass = await postPublishExam(examId, false);
            } catch (err) {
                notify(err?.message || "Publish failed.", { type: "error", title: "Publish failed" });
                return;
            }

            if (firstPass?.published) {
                setPublishedStateUi(true);
                await showEditorModal({
                    title: "Published",
                    message: firstPass.message || "Exam is now published and visible to students.",
                    confirmText: "OK",
                    hideCancel: true
                });
                return;
            }

            const blockingErrors = toIssueDetails(firstPass?.errors);
            if (blockingErrors.length > 0) {
                const firstErrorQuestionId = getFirstIssueQuestionId(firstPass?.errors);
                const firstErrorQuestionNumber = getFirstIssueQuestionNumber(firstPass?.errors);

                const goToIssue = await showEditorModal({
                    title: "Cannot publish yet",
                    message: "Fix these required items before publishing:",
                    details: blockingErrors,
                    confirmText: firstErrorQuestionId > 0 ? `Go to question ${firstErrorQuestionNumber || ""}`.trim() : "Back to editor",
                    cancelText: "Stay here",
                    hideCancel: !(firstErrorQuestionId > 0)
                });

                if (goToIssue && firstErrorQuestionId > 0) {
                    await loadQuestion(firstErrorQuestionId, { skipExitGuard: true });
                }

                return;
            }

            const warnings = toIssueDetails(firstPass?.warnings);
            if ((firstPass?.requiresWarningConfirmation || warnings.length > 0) && firstPass?.canPublish) {
                const publishWithWarnings = await showEditorModal({
                    title: "Publish with warnings",
                    message: "This exam can be published, but there are warnings:",
                    details: warnings,
                    confirmText: "Publish anyway",
                    cancelText: "Fix first",
                    confirmVariant: "danger"
                });

                if (!publishWithWarnings) return;

                let secondPass;
                try {
                    secondPass = await postPublishExam(examId, true);
                } catch (err) {
                    notify(err?.message || "Publish failed.", { type: "error", title: "Publish failed" });
                    return;
                }

                if (secondPass?.published) {
                    setPublishedStateUi(true);
                    await showEditorModal({
                        title: "Published",
                        message: secondPass.message || "Exam is now published and visible to students.",
                        confirmText: "OK",
                        hideCancel: true
                    });
                    return;
                }
            }

            notify("Publish could not be completed.", { type: "error", title: "Publish failed" });
        } finally {
            publishInProgress = false;
            setPublishButtonLoading(false);
        }
    }

    async function unpublishExam() {
        if (unpublishInProgress) return;

        const examId = getExamId();
        if (!examId) {
            notify("Missing exam id.", { type: "error", title: "Unpublish failed" });
            return;
        }

        const confirmed = await showEditorModal({
            title: "Unpublish exam",
            message: "This exam will be hidden from students until you publish it again.",
            confirmText: "Unpublish",
            cancelText: "Cancel",
            confirmVariant: "danger"
        });

        if (!confirmed) return;

        unpublishInProgress = true;
        setUnpublishButtonLoading(true);

        try {
            const result = await postUnpublishExam(examId);
            if (!result?.success) {
                notify(result?.errorMessage || "Unpublish failed.", { type: "error", title: "Unpublish failed" });
                return;
            }

            setPublishedStateUi(false);
            notify(result?.message || "Exam unpublished.", { type: "success", title: "Unpublished" });
        } catch (err) {
            notify(err?.message || "Unpublish failed.", { type: "error", title: "Unpublish failed" });
        } finally {
            unpublishInProgress = false;
            setUnpublishButtonLoading(false);
        }
    }

    window.saveExam = saveExam;
    window.publishExam = publishExam;
    window.unpublishExam = unpublishExam;
    window.toggleExamPublishState = toggleExamPublishState;

    window.addEventListener("unhandledrejection", (e) => {
        notify(
            e.reason?.message || "Unexpected error occurred.",
            { type: "error", title: "Unexpected error" }
        );
    });

    window.addEventListener("beforeunload", (e) => {
        if (!shouldWarnBeforeUnload()) return;

        // Modern browsers ignore custom text, but require returnValue to be set.
        e.preventDefault();
        e.returnValue = "";
    });

    window.examEditor = { loadQuestion, addDraft, saveExam, publishExam, unpublishExam, toggleExamPublishState };
})();






















