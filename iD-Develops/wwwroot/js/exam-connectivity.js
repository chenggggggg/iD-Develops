// wwwroot/js/exam-connectivity.js
(() => {
    "use strict";

    const STATE = {
        ONLINE: "online",
        OFFLINE: "offline",
        RECOVERED: "recovered",
        AUTH: "auth"
    };

    let currentState = STATE.ONLINE;
    let bannerEl = null;
    let pollTimer = null;
    let lastMessage = "";
    let recoverHideTimer = null;

    function ensureBanner() {
        if (bannerEl) return bannerEl;

        bannerEl = document.createElement("div");
        bannerEl.id = "exam-connection-banner";
        bannerEl.setAttribute("role", "status");
        bannerEl.style.position = "fixed";
        bannerEl.style.left = "0";
        bannerEl.style.right = "0";
        bannerEl.style.bottom = "0";
        bannerEl.style.zIndex = "9999";
        bannerEl.style.padding = "10px 14px";
        bannerEl.style.fontSize = "14px";
        bannerEl.style.display = "none";
        bannerEl.style.boxShadow = "0 -2px 10px rgba(0,0,0,0.12)";

        // Keep styling neutral (no dependency on Bootstrap classes required).
        bannerEl.style.background = "#111";
        bannerEl.style.color = "#fff";

        bannerEl.innerHTML = `
          <div style="display:flex; align-items:center; justify-content:space-between; gap:12px;">
            <div>
              <strong data-title></strong>
              <span data-msg style="opacity:0.9; margin-left:8px;"></span>
            </div>
            <div style="display:flex; align-items:center; gap:8px;">
              <button type="button" data-action="reload"
                      style="display:none; padding:6px 10px; border:0; border-radius:6px; cursor:pointer;">
                Refresh
              </button>
              <button type="button" data-action="dismiss"
                      style="padding:6px 10px; border:0; border-radius:6px; cursor:pointer;">
                Dismiss
              </button>
            </div>
          </div>
        `;

        document.body.appendChild(bannerEl);

        bannerEl.querySelector('[data-action="dismiss"]').addEventListener("click", () => {
            // For offline/auth states, we usually keep it visible. Dismiss should only hide if online/recovered.
            if (currentState === STATE.ONLINE) hideBanner();
        });

        bannerEl.querySelector('[data-action="reload"]').addEventListener("click", () => {
            window.location.reload();
        });

        return bannerEl;
    }

    function setSubmitDisabled(disabled) {
        const btn = document.querySelector("#submitExam");
        if (!btn) return;
        btn.disabled = !!disabled;
        btn.style.opacity = disabled ? "0.6" : "";
        btn.style.cursor = disabled ? "not-allowed" : "";
        btn.title = disabled ? "Submitting is disabled while the server is unavailable." : "";
    }

    function showBanner(title, msg, { showReload = false } = {}) {
        const el = ensureBanner();

        const titleEl = el.querySelector("[data-title]");
        const msgEl = el.querySelector("[data-msg]");
        const reloadBtn = el.querySelector('[data-action="reload"]');

        titleEl.textContent = title || "";
        msgEl.textContent = msg || "";

        reloadBtn.style.display = showReload ? "" : "none";

        el.style.display = "block";
        lastMessage = `${title} ${msg}`.trim();
    }

    function hideBanner() {
        if (!bannerEl) return;
        bannerEl.style.display = "none";
    }

    async function pingHealthDb() {
        // Must be treated as AJAX to avoid redirects
        const res = await fetch("/health/db", {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "application/json" },
            credentials: "same-origin",
            cache: "no-store"
        });
        return res.ok;
    }

    function startPolling() {
        if (pollTimer) return;

        pollTimer = setInterval(async () => {
            try {
                const ok = await pingHealthDb();
                if (ok) {
                    setRecovered("Back online — saving resumed.");
                }
            } catch {
                // stay offline
            }
        }, 5000);
    }

    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    function setOffline(message) {
        currentState = STATE.OFFLINE;
        clearTimeout(recoverHideTimer);
        setSubmitDisabled(true);
        showBanner(
            "Reconnecting…",
            message || "Server temporarily unavailable. Your answers may not be saved.",
            { showReload: false }
        );
        startPolling();
    }

    function setRecovered(message) {
        currentState = STATE.RECOVERED;
        stopPolling();
        setSubmitDisabled(false);
        showBanner("Back online", message || "Saving resumed.", { showReload: false });

        clearTimeout(recoverHideTimer);
        recoverHideTimer = setTimeout(() => {
            // After recovery, we hide automatically
            currentState = STATE.ONLINE;
            hideBanner();
        }, 2500);
    }

    function setAuthExpired(message) {
        currentState = STATE.AUTH;
        stopPolling();
        setSubmitDisabled(true);
        showBanner(
            "Session expired",
            message || "Please refresh and sign in again to continue.",
            { showReload: true }
        );
    }

    function classifyFailure(res, err) {
        // Use response if present; otherwise err indicates network failure.
        if (err) return "network";

        if (!res) return "unknown";

        if (res.status === 401 || res.status === 403) return "auth";
        if (res.status === 503 || res.status === 502 || res.status === 504 || res.status === 408) return "offline";
        return "other";
    }

    // Public API
    window.ExamConnectivity = {
        setOffline,
        setRecovered,
        setAuthExpired,

        // Helper that callers can use after any fetch
        handleFailure(res, err, contextMessage) {
            const kind = classifyFailure(res, err);

            if (kind === "auth") {
                setAuthExpired(contextMessage || "Please refresh and sign in again to continue.");
                return;
            }

            if (kind === "offline" || kind === "network") {
                setOffline(contextMessage || "Server temporarily unavailable. Retrying…");
                return;
            }

            // For other errors, do not spam banners. You can decide to show something else.
        },

        // Optional: allow callers to mark a successful save as recovered if we were offline
        noteSuccess() {
            if (currentState === STATE.OFFLINE) {
                setRecovered("Back online — saving resumed.");
            }
        },

        getState() { return currentState; },
        getLastMessage() { return lastMessage; }
    };
})();
