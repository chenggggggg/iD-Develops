document.addEventListener("DOMContentLoaded", function () {
    const panel = document.querySelector("[data-course-panel]");
    const toggle = document.querySelector("[data-course-panel-toggle]");
    const close = document.querySelector("[data-course-panel-close]");
    const collapse = document.querySelector("[data-course-panel-collapse]");
    const backdrop = document.querySelector("[data-course-panel-backdrop]");
    const desktopMedia = window.matchMedia("(min-width: 64rem)");
    const collapsedStorageKey = "course-content-panel-collapsed";
    const sectionToggles = Array.from(document.querySelectorAll("[data-course-section-toggle]"));
    const completionForms = Array.from(document.querySelectorAll("[data-course-completion-form]"));
    const statusMessage = document.querySelector("[data-course-status]");
    const progress = document.querySelector("[data-course-progress]");

    function setStatus(message) {
        if (statusMessage) {
            statusMessage.textContent = message || "";
        }
    }

    function updateProgress(wasCompleted, isCompleted) {
        if (!progress || wasCompleted === isCompleted) {
            return;
        }

        const total = Number(progress.dataset.total) || 0;
        const current = Number(progress.dataset.completed) || 0;
        const completed = Math.max(0, Math.min(total, current + (isCompleted ? 1 : -1)));
        const percentage = total > 0 ? Math.round(completed * 100 / total) : 0;
        const count = progress.querySelector("[data-course-progress-count]");
        const bar = progress.querySelector("[data-course-progress-bar]");
        const track = bar?.closest(".course-progress-track");

        progress.dataset.completed = String(completed);
        if (count) {
            count.textContent = `${completed}/${total}`;
        }
        if (bar) {
            bar.style.width = `${percentage}%`;
        }
        track?.setAttribute("aria-label", `${percentage}% complete`);
    }

    function setCompletionState(form, isCompleted) {
        const state = form.querySelector('input[name="completionState"]');
        const button = form.querySelector('button[type="submit"]');
        if (!state || !button) {
            return;
        }

        state.value = isCompleted ? "0" : "1";
        button.classList.toggle("is-complete", isCompleted);
        button.setAttribute("aria-checked", isCompleted ? "true" : "false");

        const title = form.dataset.contentTitle || "item";
        const action = isCompleted ? "Mark incomplete" : "Mark complete";
        button.setAttribute("aria-label", `${action} ${title}`);
        button.setAttribute("title", action);

        if (form.classList.contains("course-outline-completion-form")) {
            button.innerHTML = isCompleted
                ? '<i class="fa-solid fa-check" aria-hidden="true"></i>'
                : "";
        } else {
            button.innerHTML = isCompleted
                ? '<i class="fa-solid fa-circle-check" aria-hidden="true"></i> Mark incomplete'
                : '<i class="fa-regular fa-circle" aria-hidden="true"></i> Mark complete';
        }
    }

    completionForms.forEach(function (form) {
        form.addEventListener("submit", async function (event) {
            event.preventDefault();

            const state = form.querySelector('input[name="completionState"]');
            const button = form.querySelector('button[type="submit"]');
            if (!state || !button || button.disabled) {
                return;
            }

            const wasCompleted = state.value === "0";
            button.disabled = true;
            button.setAttribute("aria-busy", "true");

            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body: new FormData(form),
                    credentials: "same-origin",
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                const result = await response.json();
                if (!response.ok || !result.success) {
                    throw new Error(result.message || "Progress could not be updated.");
                }

                completionForms
                    .filter(candidate =>
                        candidate.dataset.contentKind === form.dataset.contentKind &&
                        candidate.dataset.contentId === form.dataset.contentId)
                    .forEach(candidate => setCompletionState(candidate, result.isCompleted));
                updateProgress(wasCompleted, result.isCompleted);
                setStatus(result.message);
            } catch (error) {
                setStatus(error instanceof Error ? error.message : "Progress could not be updated.");
            } finally {
                completionForms
                    .filter(candidate =>
                        candidate.dataset.contentKind === form.dataset.contentKind &&
                        candidate.dataset.contentId === form.dataset.contentId)
                    .forEach(candidate => {
                        const candidateButton = candidate.querySelector('button[type="submit"]');
                        if (candidateButton) {
                            candidateButton.disabled = false;
                            candidateButton.removeAttribute("aria-busy");
                        }
                    });
            }
        });
    });

    if (!panel || !toggle || !backdrop) {
        return;
    }

    function setPanelOpen(open) {
        panel.classList.toggle("is-open", open);
        backdrop.hidden = !open;
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        document.body.classList.toggle("course-panel-open", open);
    }

    function setPanelCollapsed(collapsed, persist) {
        document.body.classList.toggle("course-panel-collapsed", collapsed);
        toggle.setAttribute("aria-expanded", collapsed ? "false" : "true");
        panel.toggleAttribute("inert", collapsed);
        panel.setAttribute("aria-hidden", collapsed ? "true" : "false");

        if (persist) {
            try {
                window.localStorage.setItem(collapsedStorageKey, collapsed ? "true" : "false");
            } catch {
                // The layout still works when storage is unavailable.
            }
        }
    }

    if (desktopMedia.matches) {
        try {
            setPanelCollapsed(window.localStorage.getItem(collapsedStorageKey) === "true", false);
        } catch {
            setPanelCollapsed(false, false);
        }
    }

    toggle.addEventListener("click", function () {
        if (desktopMedia.matches) {
            setPanelCollapsed(false, true);
        } else {
            setPanelOpen(!panel.classList.contains("is-open"));
        }
    });

    collapse?.addEventListener("click", function () {
        setPanelCollapsed(true, true);
    });

    close?.addEventListener("click", function () {
        setPanelOpen(false);
    });

    backdrop.addEventListener("click", function () {
        setPanelOpen(false);
    });

    desktopMedia.addEventListener("change", function (event) {
        setPanelOpen(false);
        if (!event.matches) {
            setPanelCollapsed(false, false);
        } else {
            try {
                setPanelCollapsed(window.localStorage.getItem(collapsedStorageKey) === "true", false);
            } catch {
                setPanelCollapsed(false, false);
            }
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            setPanelOpen(false);
        }
    });

    sectionToggles.forEach(function (sectionToggle) {
        sectionToggle.addEventListener("click", function () {
            const sectionId = sectionToggle.dataset.courseSectionToggle;
            const content = document.querySelector(`[data-course-section-content="${sectionId}"]`);
            if (!content) {
                return;
            }

            const expanded = sectionToggle.getAttribute("aria-expanded") !== "true";
            content.hidden = !expanded;
            sectionToggle.setAttribute("aria-expanded", expanded ? "true" : "false");
            sectionToggle.setAttribute(
                "aria-label",
                `${expanded ? "Collapse" : "Expand"} ${sectionToggle.dataset.courseSectionTitle || "section"}`);
            sectionToggle.querySelector("i")?.classList.toggle("fa-chevron-down", expanded);
            sectionToggle.querySelector("i")?.classList.toggle("fa-chevron-right", !expanded);
        });
    });
});
